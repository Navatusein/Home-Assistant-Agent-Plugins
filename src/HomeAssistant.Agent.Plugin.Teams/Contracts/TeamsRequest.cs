namespace HomeAssistant.Agent.Plugin.Teams.Contracts;

public class TeamsRequest
{
    public int RequestId { get; set; }

    public string Action { get; set; } = null!;

    public TeamsRequestParameters Parameters { get; set; } = new();
}
