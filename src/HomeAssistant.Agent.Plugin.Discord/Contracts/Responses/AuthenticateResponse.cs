using System.Text.Json.Serialization;

namespace HomeAssistant.Agent.Plugin.Discord.Contracts.Responses;

public class AuthenticateResponse
{
    [JsonPropertyName("user")]
    public User? User { get; set; }

    [JsonPropertyName("scopes")]
    public string[]? Scopes { get; set; }

    [JsonPropertyName("expires")]
    public string? Expires { get; set; }

    [JsonPropertyName("application")]
    public object? Application { get; set; }
}