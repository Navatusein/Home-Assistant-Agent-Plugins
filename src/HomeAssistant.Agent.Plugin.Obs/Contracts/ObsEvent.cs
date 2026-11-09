using System.Text.Json;

namespace HomeAssistant.Agent.Plugin.Obs.Contracts;

// op 5
internal sealed class ObsEvent
{
    public string? EventType { get; set; }

    public JsonElement EventData { get; set; }
}
