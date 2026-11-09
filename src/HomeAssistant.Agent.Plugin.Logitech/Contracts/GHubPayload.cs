namespace HomeAssistant.Agent.Plugin.Logitech.Contracts;

internal sealed class GHubPayload
{
    // /devices/list
    public GHubDeviceInfo[]? DeviceInfos { get; set; }

    // /battery/<id>/state and /battery/state/changed
    public string? DeviceId { get; set; }

    public double? Percentage { get; set; }

    public bool? Charging { get; set; }
}
