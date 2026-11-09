using System.Net.WebSockets;
using System.Text.Json;
using HomeAssistant.Agent.Plugin.Base.Net;
using HomeAssistant.Agent.Plugin.Logitech.Contracts;
using Microsoft.Extensions.Logging;

namespace HomeAssistant.Agent.Plugin.Logitech.GHub;

// Reads battery levels from the local Logitech G HUB agent.
//
// The agent exposes an undocumented websocket on port 9010 speaking a small
// verb/path protocol. Behaviour mirrors LGSTrayBattery, which is the reference
// for this: subscribe to battery changes, ask for the device list, then ask for
// each device's current state (subscriptions only carry later changes).
// Connecting, waiting for G HUB and reconnecting is ReconnectingWebSocketClient's.
public sealed class GHubClient : ReconnectingWebSocketClient
{
    private static readonly Uri ServerUri = new("ws://localhost:9010");

    // lghub_agent.exe is the background service that hosts the websocket; the
    // lghub.exe window is only the UI and may well be closed while the agent
    // keeps running.
    private const string AgentProcessName = "lghub_agent";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    // All events fire on the connection loop's background thread.
    public event Action<GHubBattery>? BatteryUpdated;
    public event Action<IReadOnlyList<GHubDevice>>? DevicesDiscovered;

    public GHubClient(ILogger logger) : base("Logitech G HUB", logger)
    {
    }

    protected override bool? IsAppRunning() => LocalApp.IsRunningOnWindows(AgentProcessName);

    protected override Uri GetUri() => ServerUri;

    // The agent only completes the handshake for a client that looks like its
    // own web UI — the origin and the json subprotocol are both required
    // (taken from LGSTrayBattery).
    protected override void ConfigureOptions(ClientWebSocketOptions options)
    {
        options.UseDefaultCredentials = false;
        options.SetRequestHeader("Origin", "file://");
        options.SetRequestHeader("Pragma", "no-cache");
        options.SetRequestHeader("Cache-Control", "no-cache");
        options.AddSubProtocol("json");
    }

    protected override async Task OnOpenedAsync(CancellationToken cancellationToken)
    {
        MarkConnected();

        // Subscriptions only deliver later changes, so the current state of
        // every device still has to be asked for explicitly (see HandleDeviceListAsync).
        await SendAsync("SUBSCRIBE", "/battery/state/changed", cancellationToken);
        await SendAsync("GET", "/devices/list", cancellationToken);
    }

    protected override async Task OnMessageAsync(string json, CancellationToken cancellationToken)
    {
        GHubMessage? message;

        try
        {
            message = JsonSerializer.Deserialize<GHubMessage>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return;
        }

        if (message?.Path == null || message.Payload == null)
            return;

        if (message.Path == "/devices/list")
        {
            await HandleDeviceListAsync(message.Payload, cancellationToken);
            return;
        }

        // Battery data arrives both as the answer to an explicit GET
        // (/battery/<id>/state) and as a push (/battery/state/changed).
        if (message.Path == "/battery/state/changed" || IsBatteryStatePath(message.Path))
            HandleBatteryUpdate(message.Payload);
    }

    private async Task HandleDeviceListAsync(GHubPayload payload, CancellationToken cancellationToken)
    {
        var devices = (payload.DeviceInfos ?? [])
            .Where(info => info.Id != null)
            .Select(info => new GHubDevice(
                info.Id!,
                info.ExtendedDisplayName ?? info.Id!,
                info.Capabilities?.HasBatteryStatus ?? false))
            .ToArray();

        // Logged so the names to put in the settings can simply be copied from
        // the Logs page — the ids are opaque and nothing else shows them.
        Logger.LogInformation("G HUB reported {Count} device(s): {Devices}",
            devices.Length,
            string.Join(", ", devices.Select(d => $"{d.DisplayName} [{d.Id}]{(d.HasBattery ? string.Empty : " (no battery)")}")));

        DevicesDiscovered?.Invoke(devices);

        foreach (var device in devices.Where(device => device.HasBattery))
            await SendAsync("GET", $"/battery/{device.Id}/state", cancellationToken);
    }

    private void HandleBatteryUpdate(GHubPayload payload)
    {
        if (payload.DeviceId == null || payload.Percentage == null)
            return;

        // Logged because an unconfigured device is dropped silently further up:
        // this is what answers "G HUB sees it, so why is there no sensor?".
        Logger.LogDebug("Battery {Device}: {Percentage}% (charging: {Charging})",
            payload.DeviceId, payload.Percentage, payload.Charging ?? false);

        BatteryUpdated?.Invoke(new GHubBattery(
            payload.DeviceId,
            payload.Percentage.Value,
            payload.Charging ?? false));
    }

    // "/battery/dev00000001/state"
    private static bool IsBatteryStatePath(string path) =>
        path.StartsWith("/battery/", StringComparison.Ordinal)
        && path.EndsWith("/state", StringComparison.Ordinal);

    private Task<bool> SendAsync(string verb, string path, CancellationToken cancellationToken) =>
        SendTextAsync(JsonSerializer.Serialize(new GHubRequest { Verb = verb, Path = path }, JsonOptions), cancellationToken);
}
