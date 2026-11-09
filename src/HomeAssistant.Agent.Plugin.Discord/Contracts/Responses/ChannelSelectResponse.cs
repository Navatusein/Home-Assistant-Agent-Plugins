using System.Text.Json.Serialization;

namespace HomeAssistant.Agent.Plugin.Discord.Contracts.Responses;

public class ChannelSelectResponse
{
    [JsonPropertyName("channel_id")]
    public string? Id { get; set; }

    [JsonPropertyName("guild_id")]
    public string? GuildId { get; set; }
}