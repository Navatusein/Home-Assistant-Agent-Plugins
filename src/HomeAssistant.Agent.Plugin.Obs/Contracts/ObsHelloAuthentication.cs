namespace HomeAssistant.Agent.Plugin.Obs.Contracts;

internal sealed class ObsHelloAuthentication
{
    public string Challenge { get; set; } = string.Empty;

    public string Salt { get; set; } = string.Empty;
}
