using HomeAssistant.Agent.Plugin.Base.Interfaces;

namespace HomeAssistant.Agent.Plugin.Teams.Config;

public class TeamsPluginConfig : IPluginConfig
{
    // Where the Teams client runs; the local API only listens on port 8124.
    public string Host { get; set; } = "localhost";
}
