using System.Text.Json.Serialization;

namespace HomeAssistant.Agent.Plugin.Discord.Contracts;

public class User
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = null!;

    [JsonPropertyName("username")]
    public string Username { get; set; } = null!;

    [JsonPropertyName("discriminator")]
    public string Discriminator { get; set; } = null!;

    [JsonPropertyName("avatar")]
    public string Avatar { get; set; } = null!;

    [JsonPropertyName("bot")]
    public bool IsBot { get; set; }
}