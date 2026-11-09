using System.Text.Json;
using System.Text.Json.Serialization;

namespace HomeAssistant.Agent.Plugin.Obs.Contracts;

// obs-websocket 5 wraps everything in the same op/d envelope; the shape of the
// payload depends on the op code, so it is kept raw here and parsed per case.
internal sealed class ObsEnvelope
{
    [JsonPropertyName("op")]
    public int Op { get; set; }

    [JsonPropertyName("d")]
    public JsonElement Data { get; set; }
}
