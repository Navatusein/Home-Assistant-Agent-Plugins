namespace HomeAssistant.Agent.Plugin.Logitech.GHub;

// The last battery reading seen for a device.
public sealed record GHubBattery(string DeviceId, double Percentage, bool Charging);
