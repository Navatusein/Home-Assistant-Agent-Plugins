namespace HomeAssistant.Agent.Plugin.Obs.Contracts;

// op 0 — sent by OBS on connect. Authentication is absent when the server runs
// without a password.
internal sealed class ObsHello
{
    public string? ObsWebSocketVersion { get; set; }

    public int RpcVersion { get; set; }

    public ObsHelloAuthentication? Authentication { get; set; }
}
