using System.Text.Json;
using System.Text.Json.Serialization;
using HomeAssistant.Agent.Plugin.Base.Net;
using HomeAssistant.Agent.Plugin.Teams.Contracts;
using Microsoft.Extensions.Logging;

namespace HomeAssistant.Agent.Plugin.Teams.Teams;

// Talks to the local "third-party devices" API of the new Microsoft Teams
// client. The API must be enabled in Teams (Settings -> Privacy -> Manage
// API). Meeting state is pushed even to unpaired clients; commands require
// pairing: on the first command attempt during a meeting Teams shows an
// allow prompt and answers with a token that authorizes future connections.
// Connecting, waiting for Teams and reconnecting is ReconnectingWebSocketClient's.
public sealed class TeamsLocalClient : ReconnectingWebSocketClient
{
    private const string UriPattern =
        "ws://{0}:8124?token={1}&protocol-version=2.0.0&manufacturer={2}&device={3}&app={4}&app-version={5}";

    // The new Teams client (MSTeams package) runs as ms-teams.exe. Classic
    // Teams was Teams.exe and is deliberately not matched: it has no local
    // API on port 8124, so there would be nothing to connect to.
    private const string ProcessName = "ms-teams";

    // After a 403 the app is blocked (or the token was revoked) in Teams —
    // back off so the user has time to unblock it.
    private static readonly TimeSpan BlockedDelay = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _host;
    private readonly bool _checkLocalProcess;
    private readonly string _appName;
    private readonly string _appVersion;

    private string _token;
    private int _requestId;
    private int _pairRequestId = -1;

    // Whether Teams has accepted this connection as a paired client. Teams
    // never reports authorization directly: it simply starts pushing
    // meetingState (and stops offering the pairing prompt) once the client is
    // recognized. Reset on every connection attempt.
    private bool _authorized;

    public TeamsMeetingState? MeetingState { get; private set; }

    // All events fire on the connection loop's background thread.
    public event Action? MeetingStateChanged;
    public event Func<string, Task>? TokenRefreshed;

    public TeamsLocalClient(string host, string? token, string appName, string appVersion, ILogger logger)
        : base("Teams", logger)
    {
        _host = host;
        _checkLocalProcess = LocalApp.IsLocalHost(host);
        _token = token ?? string.Empty;
        _appName = appName;
        _appVersion = appVersion;
    }

    public Task ToggleMuteAsync() => SendAsync("toggle-mute");

    public Task ToggleVideoAsync() => SendAsync("toggle-video");

    protected override TimeSpan ReconnectDelay => TimeSpan.FromSeconds(5);

    protected override TimeSpan AppPollInterval => TimeSpan.FromSeconds(3);

    // Nothing listens on port 8124 while Teams is closed. Only meaningful for
    // a local host — a remote one runs Teams on a machine whose process list
    // is none of our business.
    protected override bool? IsAppRunning() => _checkLocalProcess ? LocalApp.IsRunningOnWindows(ProcessName) : null;

    protected override Uri GetUri()
    {
        Logger.LogDebug("Connecting to the Teams local API ({Pairing})",
            _token.Length > 0 ? "with a stored token" : "unpaired");

        return new Uri(string.Format(UriPattern,
            _host,
            Uri.EscapeDataString(_token),
            Uri.EscapeDataString(_appName),
            Uri.EscapeDataString(Environment.MachineName),
            Uri.EscapeDataString(_appName),
            Uri.EscapeDataString(_appVersion)));
    }

    protected override Task OnOpenedAsync(CancellationToken cancellationToken)
    {
        _authorized = false;
        MarkConnected();
        return Task.CompletedTask;
    }

    protected override async Task<TimeSpan?> OnConnectionFailedAsync(Exception exception)
    {
        if (!exception.ToString().Contains("403"))
            return await base.OnConnectionFailedAsync(exception);

        Logger.LogWarning("Teams refused the connection (403) — the app is blocked or the token was revoked");

        // Blocked or the token was revoked: drop the token so the next
        // connection can go through the pairing flow again.
        if (_token.Length > 0)
            await ClearTokenAsync();

        _pairRequestId = -1;
        return BlockedDelay;
    }

    protected override void OnDisconnected(bool wasConnected)
    {
        MeetingState = null;
        _authorized = false;

        if (wasConnected)
            MeetingStateChanged?.Invoke();
    }

    protected override async Task OnMessageAsync(string json, CancellationToken cancellationToken)
    {
        TeamsServerMessage? message;

        try
        {
            message = JsonSerializer.Deserialize<TeamsServerMessage>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return;
        }

        if (message == null)
            return;

        if (message.TokenRefresh != null)
        {
            Logger.LogInformation("Teams paired the plugin and issued a token");

            _token = message.TokenRefresh;
            _pairRequestId = -1;

            if (TokenRefreshed != null)
                await TokenRefreshed.Invoke(message.TokenRefresh);

            // Teams pushes meetingState only to authorized connections, and
            // the current one was opened unauthorized — reconnect so the new
            // token takes effect (verified against Teams 2.x live).
            Reconnect();
            return;
        }

        if (message.MeetingUpdate is { } update)
        {
            if (update.MeetingState != null)
            {
                _authorized = true;
                MeetingState = update.MeetingState;
                MeetingStateChanged?.Invoke();
            }
            // Teams offers the pairing prompt only to clients it does not
            // recognize, so canPair on a connection that never received a
            // meetingState means this client is unpaired — whatever token it
            // presented. Never gate this on holding no token: a token revoked
            // in Teams (or invalidated by a Teams update) is still accepted at
            // the handshake, so the plugin would otherwise stay connected but
            // permanently unauthorized, with no meeting state and dead
            // switches, and no 403 to recover from.
            else if (!_authorized && update.MeetingPermissions is { CanPair: true })
            {
                // Pair from a clean state: Teams answers the pairing request
                // on an unauthenticated connection, so a stale token has to go
                // first.
                if (_token.Length > 0)
                {
                    Logger.LogWarning("Teams does not recognize the stored token — discarding it and re-pairing");

                    await ClearTokenAsync();
                    Reconnect();
                    return;
                }

                // While Teams is ready to show the pairing prompt, any request
                // triggers it — a "like" reaction is the least invasive.
                if (_pairRequestId < 0)
                {
                    Logger.LogInformation("Requesting pairing — allow the plugin in the Teams prompt");
                    _pairRequestId = await SendAsync("send-reaction", "like", cancellationToken);
                }
            }
        }

        // A failed pairing request must not block another attempt.
        if (message.ErrorMsg != null && message.RequestId == _pairRequestId)
        {
            Logger.LogWarning("Teams rejected the pairing request: {Error}", message.ErrorMsg);
            _pairRequestId = -1;
        }
    }

    private async Task ClearTokenAsync()
    {
        _token = string.Empty;
        _pairRequestId = -1;

        if (TokenRefreshed != null)
            await TokenRefreshed.Invoke(string.Empty);
    }

    // Returns the request id, or -1 when there is no connection to send on.
    private async Task<int> SendAsync(string action, string? reactionType = null,
        CancellationToken cancellationToken = default)
    {
        var request = new TeamsRequest
        {
            RequestId = Interlocked.Increment(ref _requestId),
            Action = action,
            Parameters = new TeamsRequestParameters { Type = reactionType }
        };

        var sent = await SendTextAsync(JsonSerializer.Serialize(request, JsonOptions), cancellationToken);

        return sent ? request.RequestId : -1;
    }
}
