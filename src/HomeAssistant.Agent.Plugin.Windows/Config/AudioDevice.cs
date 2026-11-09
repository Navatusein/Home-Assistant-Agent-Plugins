namespace HomeAssistant.Agent.Plugin.Windows.Config;

public class AudioDevice
{
    // The device name as shown in Windows sound settings, used to match the
    // real endpoint. For playback this is the "sound" (media/game) device.
    public string DeviceName { get; set; } = null!;

    // Playback only, optional: a separate device to use as the default
    // "communication" endpoint (e.g. chat channel of a headset). When null the
    // same device is used for both sound and communication.
    public string? CommunicationDeviceName { get; set; }

    // The label shown as the option in Home Assistant. Falls back to DeviceName.
    public string? FriendlyName { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(FriendlyName) ? DeviceName : FriendlyName;
}
