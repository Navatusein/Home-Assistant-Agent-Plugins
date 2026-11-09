namespace HomeAssistant.Agent.Plugin.Teams.Contracts;

public class TeamsMeetingPermissions
{
    public bool CanToggleMute { get; set; }

    public bool CanToggleVideo { get; set; }

    // True while Teams would show the pairing prompt for a request from an
    // unpaired client (only during a meeting).
    public bool CanPair { get; set; }
}
