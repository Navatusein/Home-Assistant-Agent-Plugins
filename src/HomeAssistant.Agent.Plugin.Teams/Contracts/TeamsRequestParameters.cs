namespace HomeAssistant.Agent.Plugin.Teams.Contracts;

public class TeamsRequestParameters
{
    // Only used by "send-reaction" (like/love/...); null for toggles.
    public string? Type { get; set; }
}
