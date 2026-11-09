using System.Text.Json;

namespace HomeAssistant.Agent.Plugin.Obs.Contracts;

// op 7
internal sealed class ObsRequestResponse
{
    public string? RequestType { get; set; }

    public string? RequestId { get; set; }

    public ObsRequestStatus? RequestStatus { get; set; }

    public JsonElement ResponseData { get; set; }
}
