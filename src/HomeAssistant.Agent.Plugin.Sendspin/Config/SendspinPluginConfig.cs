using HomeAssistant.Agent.Plugin.Base.Interfaces;

namespace HomeAssistant.Agent.Plugin.Sendspin.Config;

public class SendspinPluginConfig : IPluginConfig
{
    // Shown in Music Assistant; the computer name when not set.
    public string PlayerName { get; set; } = string.Empty;
}
