namespace HomeAssistant.Agent.Plugin.Obs.Contracts;

// GetSceneList
internal sealed class ObsSceneList
{
    public string? CurrentProgramSceneName { get; set; }

    public ObsSceneEntry[]? Scenes { get; set; }
}
