using HomeAssistant.Agent.Plugin.Base.Interfaces;

namespace HomeAssistant.Agent.Plugin.Obs.Config;

public class ObsPluginConfig : IPluginConfig
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 4455;

    // Empty when the OBS WebSocket server runs without authentication.
    public string Password { get; set; } = string.Empty;
}