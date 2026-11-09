using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HomeAssistant.Agent.Plugin.Base.Net;
using HomeAssistant.Agent.Plugin.Obs.Contracts;
using Microsoft.Extensions.Logging;

namespace HomeAssistant.Agent.Plugin.Obs.Obs;

// Talks to the OBS Studio WebSocket server (obs-websocket 5, shipped with OBS
// 28 and later, Tools -> WebSocket Server Settings).
//
// The protocol is a JSON op/d envelope: OBS greets with Hello (op 0), the
// client answers Identify (op 1) — hashing the password against the challenge
// when the server asks for authentication — and OBS confirms with Identified
// (op 2). After that, requests (op 6) are answered by a response (op 7)
// carrying the request id back, and subscribed events arrive as op 5.
// Connecting, waiting for OBS and reconnecting is ReconnectingWebSocketClient's.
public sealed class ObsClient : ReconnectingWebSocketClient
{
    private const int RpcVersion = 1;

    // Scenes (1 << 2) and Outputs (1 << 6) — the scene switches and the
    // recording state. Everything else OBS would push (inputs, filters, scene
    // items) is traffic this plugin has no use for.
    private const int EventSubscriptions = (1 << 2) | (1 << 6);

    // obs-websocket's close code for a wrong password.
    private const int AuthenticationFailedCloseCode = 4009;

    // 64-bit OBS is obs64.exe; the other two cover 32-bit and older installs.
    private static readonly string[] ProcessNames = ["obs64", "obs32", "obs"];

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    // Requests waiting for their op 7 response, by request id.
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ObsRequestResponse?>> _pendingRequests = new();

    private readonly string _host;
    private readonly int _port;
    private readonly string _password;
    private readonly bool _isLocal;

    // Said once per wrong password, not on every reconnect.
    private bool _authenticationFailed;

    // All events fire on the connection loop's background thread.
    public event Action<IReadOnlyList<string>>? ScenesChanged;
    public event Action<string>? CurrentSceneChanged;
    public event Action<bool>? RecordingChanged;

    public ObsClient(string host, int port, string? password, ILogger logger) : base("OBS", logger)
    {
        _host = host;
        _port = port;
        _password = password ?? string.Empty;
        _isLocal = LocalApp.IsLocalHost(host);
    }

    public async Task<bool> SetCurrentSceneAsync(string sceneName)
    {
        return await RequestAsync("SetCurrentProgramScene", new { sceneName }) != null;
    }

    public async Task<bool> SetRecordingAsync(bool recording)
    {
        return await RequestAsync(recording ? "StartRecord" : "StopRecord") != null;
    }

    // The process check only says anything about OBS on this machine — a
    // remote host is probed by connecting instead.
    protected override bool? IsAppRunning() => _isLocal ? LocalApp.IsRunningOnWindows(ProcessNames) : null;

    protected override Uri GetUri() => new($"ws://{_host}:{_port}");

    protected override async Task OnMessageAsync(string json, CancellationToken cancellationToken)
    {
        ObsEnvelope? envelope;

        try
        {
            envelope = JsonSerializer.Deserialize<ObsEnvelope>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return;
        }

        if (envelope == null)
            return;

        switch (envelope.Op)
        {
            case ObsOpCode.Hello:
                await IdentifyAsync(envelope.Data, cancellationToken);
                break;

            case ObsOpCode.Identified:
                _authenticationFailed = false;
                MarkConnected();

                // Runs outside the receive loop on purpose: it waits for
                // responses that only this loop can deliver.
                _ = Task.Run(LoadInitialStateAsync, cancellationToken);
                break;

            case ObsOpCode.Event:
                HandleEvent(envelope.Data);
                break;

            case ObsOpCode.RequestResponse:
                HandleRequestResponse(envelope.Data);
                break;
        }
    }

    protected override void OnClosedByServer(WebSocketCloseStatus? status, string? description)
    {
        // A wrong password repeats on every reconnect, so it is only worth
        // saying once.
        if ((int?) status == AuthenticationFailedCloseCode)
        {
            if (_authenticationFailed)
                return;

            _authenticationFailed = true;
            Logger.LogWarning("OBS rejected the password — check the plugin settings");
            return;
        }

        base.OnClosedByServer(status, description);
    }

    // Waiting on a response the connection can no longer deliver is a hang;
    // release the callers instead.
    protected override void OnDisconnected(bool wasConnected)
    {
        foreach (var requestId in _pendingRequests.Keys)
        {
            if (_pendingRequests.TryRemove(requestId, out var pending))
                pending.TrySetResult(null);
        }
    }

    private async Task IdentifyAsync(JsonElement data, CancellationToken cancellationToken)
    {
        var hello = Parse<ObsHello>(data);

        object payload;

        if (hello?.Authentication is { } authentication)
        {
            if (_password.Length == 0 && !_authenticationFailed)
            {
                _authenticationFailed = true;
                Logger.LogWarning("OBS asks for a password — set it in the plugin settings");
            }

            payload = new
            {
                rpcVersion = RpcVersion,
                authentication = BuildAuthentication(_password, authentication),
                eventSubscriptions = EventSubscriptions
            };
        }
        else
        {
            payload = new { rpcVersion = RpcVersion, eventSubscriptions = EventSubscriptions };
        }

        await SendAsync(new { op = ObsOpCode.Identify, d = payload }, cancellationToken);
    }

    // base64(sha256(base64(sha256(password + salt)) + challenge)), as
    // obs-websocket specifies it.
    private static string BuildAuthentication(string password, ObsHelloAuthentication authentication)
    {
        var secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + authentication.Salt)));

        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + authentication.Challenge)));
    }

    // Subscriptions only carry later changes, so the current scene list and
    // recording state still have to be asked for.
    private async Task LoadInitialStateAsync()
    {
        var sceneList = Parse<ObsSceneList>((await RequestAsync("GetSceneList"))?.ResponseData);

        if (sceneList != null)
        {
            PublishScenes(sceneList.Scenes);

            if (sceneList.CurrentProgramSceneName != null)
                CurrentSceneChanged?.Invoke(sceneList.CurrentProgramSceneName);
        }

        var recordState = Parse<ObsRecordState>((await RequestAsync("GetRecordStatus"))?.ResponseData);

        if (recordState != null)
            RecordingChanged?.Invoke(recordState.OutputActive);
    }

    private void HandleEvent(JsonElement data)
    {
        var obsEvent = Parse<ObsEvent>(data);

        switch (obsEvent?.EventType)
        {
            case "CurrentProgramSceneChanged":
            {
                var sceneName = Parse<ObsSceneChanged>(obsEvent.EventData)?.SceneName;

                if (sceneName != null)
                    CurrentSceneChanged?.Invoke(sceneName);

                break;
            }

            case "SceneListChanged":
                PublishScenes(Parse<ObsSceneList>(obsEvent.EventData)?.Scenes);
                break;

            case "RecordStateChanged":
            {
                var recordState = Parse<ObsRecordState>(obsEvent.EventData);

                if (recordState != null)
                {
                    Logger.LogDebug("OBS recording state: {State}", recordState.OutputState);
                    RecordingChanged?.Invoke(recordState.OutputActive);
                }

                break;
            }
        }
    }

    private void HandleRequestResponse(JsonElement data)
    {
        var response = Parse<ObsRequestResponse>(data);

        if (response?.RequestId == null)
            return;

        if (_pendingRequests.TryRemove(response.RequestId, out var pending))
            pending.TrySetResult(response);
    }

    private void PublishScenes(ObsSceneEntry[]? scenes)
    {
        var names = (scenes ?? [])
            .Where(scene => scene.SceneName != null)
            .OrderByDescending(scene => scene.SceneIndex)
            .Select(scene => scene.SceneName!)
            .ToArray();

        // Logged so the scenes to configure can be read off the Logs page even
        // before the settings drop-down is opened.
        Logger.LogDebug("OBS reported {Count} scene(s): {Scenes}", names.Length, string.Join(", ", names));

        ScenesChanged?.Invoke(names);
    }

    // Returns the response only when OBS reports success; a refused request
    // (recording already running, unknown scene) answers null.
    private async Task<ObsRequestResponse?> RequestAsync(string requestType, object? requestData = null)
    {
        if (!IsConnected)
            return null;

        var requestId = Guid.NewGuid().ToString("N");
        var pending = new TaskCompletionSource<ObsRequestResponse?>(TaskCreationOptions.RunContinuationsAsynchronously);

        _pendingRequests[requestId] = pending;

        try
        {
            await SendAsync(new { op = ObsOpCode.Request, d = new { requestType, requestId, requestData } },
                StoppingToken);

            // Race the response against a real delay: a lost reply would
            // otherwise hang this await forever.
            var completed = await Task.WhenAny(pending.Task, Task.Delay(RequestTimeout, StoppingToken));

            if (completed != pending.Task)
            {
                Logger.LogWarning("OBS request {Request} timed out", requestType);
                return null;
            }

            var response = await pending.Task;

            if (response == null)
                return null;

            if (response.RequestStatus is { Result: true })
                return response;

            Logger.LogWarning("OBS refused {Request}: {Reason}", requestType,
                response.RequestStatus?.Comment ?? $"code {response.RequestStatus?.Code}");

            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "OBS request {Request} failed", requestType);
            return null;
        }
        finally
        {
            _pendingRequests.TryRemove(requestId, out _);
        }
    }

    private Task<bool> SendAsync(object message, CancellationToken cancellationToken) =>
        SendTextAsync(JsonSerializer.Serialize(message, JsonOptions), cancellationToken);

    private static T? Parse<T>(JsonElement? data) where T : class
    {
        if (data is not { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } element)
            return null;

        try
        {
            return element.Deserialize<T>(JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
