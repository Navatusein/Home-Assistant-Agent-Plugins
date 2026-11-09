namespace HomeAssistant.Agent.Plugin.Obs.Contracts;

// CurrentProgramSceneChanged
internal sealed class ObsSceneChanged
{
    public string? SceneName { get; set; }
}
