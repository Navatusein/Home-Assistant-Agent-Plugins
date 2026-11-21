using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace HomeAssistant.Agent.Plugin.Ntfy.Ntfy;

// A subscription to ntfy topics over its JSON stream, kept open: a lost
// connection is opened again, waiting longer each time it fails in a row.
// Only messages sent while it stands arrive — no "since", so nothing missed
// while the computer was off is shown later.
internal sealed class NtfyClient : IDisposable
{
    // ntfy sends a keepalive every 45 s by default: a stream silent for twice
    // that is dead even if the socket does not know it yet.
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan FirstRetry = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan LongestRetry = TimeSpan.FromMinutes(1);

    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly Uri _url;
    private readonly string _token;
    private readonly ILogger<NtfyClient> _logger;
    private readonly CancellationTokenSource _stopping = new();
    private Task? _run;

    public NtfyClient(string server, IReadOnlyList<string> topics, string token, string userAgent, ILogger<NtfyClient> logger)
    {
        _url = new Uri($"{server}/{string.Join(',', topics)}/json");
        _token = token;
        _logger = logger;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
    }

    public bool IsConnected { get; private set; }

    public event Action? ConnectionChanged;

    public event Action<NtfyMessage>? MessageReceived;

    public void Start() => _run = Task.Run(() => RunAsync(_stopping.Token));

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var retry = FirstRetry;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (await SubscribeAsync(cancellationToken))
                    retry = FirstRetry;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("ntfy at {Server}: {Error}", _url.GetLeftPart(UriPartial.Authority), ex.Message);
            }
            finally
            {
                SetConnected(false);
            }

            try
            {
                await Task.Delay(retry, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            retry = TimeSpan.FromTicks(Math.Min(retry.Ticks * 2, LongestRetry.Ticks));
        }
    }

    // True once the subscription stood: the next retry starts short again.
    private async Task<bool> SubscribeAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _url);

        if (_token.Length > 0)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new InvalidOperationException("the access token was refused for these topics");

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        var opened = false;

        while (true)
        {
            using var read = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            read.CancelAfter(ReadTimeout);

            string? line;

            try
            {
                line = await reader.ReadLineAsync(read.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"nothing for {ReadTimeout.TotalSeconds:0} s");
            }

            if (line == null)
                return opened;

            if (NtfyMessage.Parse(line) is not { } message)
                continue;

            if (message.Event == NtfyMessage.OpenEvent)
            {
                opened = true;
                SetConnected(true);
                _logger.LogInformation("Subscribed to {Url}", _url);
            }
            else if (message.Event == NtfyMessage.MessageEvent)
            {
                MessageReceived?.Invoke(message);
            }
        }
    }

    private void SetConnected(bool connected)
    {
        if (IsConnected == connected)
            return;

        IsConnected = connected;
        ConnectionChanged?.Invoke();
    }

    public void Dispose()
    {
        _stopping.Cancel();

        try
        {
            _run?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Stopped; how is not interesting any more.
        }

        _stopping.Dispose();
        _http.Dispose();
    }
}
