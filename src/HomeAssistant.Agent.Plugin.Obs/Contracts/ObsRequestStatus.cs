namespace HomeAssistant.Agent.Plugin.Obs.Contracts;

internal sealed class ObsRequestStatus
{
    public bool Result { get; set; }

    public int Code { get; set; }

    public string? Comment { get; set; }
}
