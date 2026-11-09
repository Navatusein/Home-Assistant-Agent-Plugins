using System.Text.Json.Serialization;

namespace HomeAssistant.Agent.Plugin.Discord.Contracts.Responses;

public class AuthorizeResponse
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = null!;
}