using HomeAssistant.Agent.Plugin.Base.Interfaces;

namespace HomeAssistant.Agent.Plugin.Logitech.Config;

public class LogitechPluginConfig : IPluginConfig
{
    // Devices to expose. Listed up front rather than discovered, for the same
    // reason the audio devices are: a mouse that is switched off disappears
    // from the G HUB device list, and its sensor should stay in Home Assistant
    // instead of vanishing with it.
    public List<LogitechDevice> Devices { get; set; } = [];
}

public class LogitechDevice
{
    // The settings form offers the display names G HUB reported, but an id
    // (dev00000000) is still accepted — entries predate the drop-down, and the
    // ids are not stable enough to migrate them to.
    public string Device { get; set; } = string.Empty;

    // Name of the sensor in Home Assistant.
    public string Name { get; set; } = string.Empty;
}