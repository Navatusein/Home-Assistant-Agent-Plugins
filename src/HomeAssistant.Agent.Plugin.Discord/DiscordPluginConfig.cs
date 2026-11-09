using HomeAssistant.Agent.Plugin.Base.Interfaces;

namespace HomeAssistant.Agent.Plugin.Discord;

public class DiscordPluginConfig: IPluginConfig
{
    public string ClientId { get; set; } = null!;
    
    public string ClientSecret { get; set; } = null!;
}