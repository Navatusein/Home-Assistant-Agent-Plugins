using System.Text.Json.Serialization;

namespace HomeAssistant.Agent.Plugin.Logitech.Contracts;

internal sealed class GHubRequest
{
    [JsonPropertyName("msgId")]
    public string MsgId { get; set; } = string.Empty;

    public string Verb { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;
}
