namespace HomeAssistant.Agent.Plugin.Obs.Contracts;

internal sealed class ObsSceneEntry
{
    public string? SceneName { get; set; }

    // OBS numbers the scenes bottom-up, so the list reads in UI order when
    // sorted descending.
    public int SceneIndex { get; set; }
}
