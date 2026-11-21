using System.Text;
using Microsoft.Extensions.Logging;

namespace HomeAssistant.Agent.Plugin.Ntfy.Ntfy;

// Sends the request of an "http" button, from this computer, as ntfy's apps
// do: the method (POST unless the message says otherwise), the headers and
// the body the message carries.
internal sealed class NtfyHttpAction(ILogger logger) : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public Func<Task> For(NtfyAction action) => () => SendAsync(action);

    private async Task SendAsync(NtfyAction action)
    {
        using var request = new HttpRequestMessage(new HttpMethod(action.Method?.ToUpperInvariant() ?? "POST"), action.Url);

        if (action.Body != null)
            request.Content = new StringContent(action.Body, Encoding.UTF8);

        foreach (var (name, value) in action.Headers ?? new Dictionary<string, string>())
        {
            // Content headers (Content-Type, …) belong to the content.
            if (!request.Headers.TryAddWithoutValidation(name, value))
            {
                request.Content ??= new ByteArrayContent([]);
                request.Content.Headers.Remove(name);
                request.Content.Headers.TryAddWithoutValidation(name, value);
            }
        }

        using var response = await _http.SendAsync(request);

        logger.LogInformation("Button {Label}: {Method} {Url} answered {Status}",
            action.Label, request.Method, request.RequestUri, (int)response.StatusCode);
    }

    public void Dispose() => _http.Dispose();
}
