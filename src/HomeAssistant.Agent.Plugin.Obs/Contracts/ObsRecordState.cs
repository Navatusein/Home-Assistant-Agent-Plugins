namespace HomeAssistant.Agent.Plugin.Obs.Contracts;

// GetRecordStatus and RecordStateChanged. OutputActive stays true while the
// recording is paused, which is what the switch in Home Assistant shows.
internal sealed class ObsRecordState
{
    public bool OutputActive { get; set; }

    public bool OutputPaused { get; set; }

    // OBS_WEBSOCKET_OUTPUT_STARTED and friends — only logged.
    public string? OutputState { get; set; }
}
