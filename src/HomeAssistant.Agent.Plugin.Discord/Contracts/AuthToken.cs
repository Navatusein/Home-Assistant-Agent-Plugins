namespace HomeAssistant.Agent.Plugin.Discord.Contracts;

public class AuthToken
{
    public string Token { get; set; } = null!;
    public string[] Scopes { get; set; } = null!;
    public DateTime Expires { get; set; }
    public string RefreshToken { get; set; } = null!;
    public string Type { get; set; } = null!;
}