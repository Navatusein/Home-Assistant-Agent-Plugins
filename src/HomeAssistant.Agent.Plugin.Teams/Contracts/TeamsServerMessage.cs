namespace HomeAssistant.Agent.Plugin.Teams.Contracts;

// DTOs for the local Teams "third-party devices" API (camelCase JSON over
// the ws://<host>:8124 socket of the new Teams client).
public class TeamsServerMessage
{
    public TeamsMeetingUpdate? MeetingUpdate { get; set; }

    public int? RequestId { get; set; }

    public string? Response { get; set; }

    public string? ErrorMsg { get; set; }

    // Sent once after the user allows the pairing prompt; authorizes all
    // future connections and must be persisted.
    public string? TokenRefresh { get; set; }
}
