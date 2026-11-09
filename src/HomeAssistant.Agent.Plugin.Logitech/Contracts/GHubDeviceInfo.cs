namespace HomeAssistant.Agent.Plugin.Logitech.Contracts;

internal sealed class GHubDeviceInfo
{
    public string? Id { get; set; }

    public string? ExtendedDisplayName { get; set; }

    public string? DeviceType { get; set; }

    public GHubCapabilities? Capabilities { get; set; }
}
