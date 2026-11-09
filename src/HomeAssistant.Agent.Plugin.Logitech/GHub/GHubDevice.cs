namespace HomeAssistant.Agent.Plugin.Logitech.GHub;

// A device as G HUB reports it.
public sealed record GHubDevice(string Id, string DisplayName, bool HasBattery);
