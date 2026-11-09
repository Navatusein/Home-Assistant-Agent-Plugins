using System.Text.Json;
using System.Text.Json.Serialization;
using HomeAssistant.Agent.Plugin.Discord.Discord;

namespace HomeAssistant.Agent.Plugin.Discord.Contracts;

// Every RPC message, both ways: a command and its response share the nonce,
// pushed events come as cmd DISPATCH with the event in evt.
public class Payload
{
    [JsonPropertyName("cmd")]
    public string? Cmd { get; set; }

    [JsonPropertyName("nonce")]
    public string? Nonce { get; set; }

    [JsonPropertyName("evt")]
    public string? Evt { get; set; }

    [JsonPropertyName("data")]
    public JsonElement? Data { get; set; }

    // Discord answered the command with an error ({"code", "message"} data).
    [JsonIgnore]
    public bool IsError => Evt == "ERROR";

    // null when there is no data or it is JSON null (not in a voice channel).
    public T? DataAs<T>() where T : class
    {
        return Data is { ValueKind: JsonValueKind.Object } data
            ? data.Deserialize<T>(DiscordJson.Options)
            : null;
    }

    // A string property of the data, or null.
    public string? GetDataString(string name)
    {
        return Data is { ValueKind: JsonValueKind.Object } data
               && data.TryGetProperty(name, out var value)
               && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
