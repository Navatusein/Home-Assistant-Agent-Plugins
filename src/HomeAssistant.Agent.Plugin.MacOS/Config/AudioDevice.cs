namespace HomeAssistant.Agent.Plugin.MacOS.Config;

public class AudioDevice
{
    // The device name as shown in macOS Sound settings, used to match the
    // real device.
    public string DeviceName { get; set; } = null!;

    // The label shown as the option in Home Assistant. Falls back to DeviceName.
    public string? FriendlyName { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(FriendlyName) ? DeviceName : FriendlyName;
}
