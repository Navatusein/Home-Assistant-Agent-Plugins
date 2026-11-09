namespace HomeAssistant.Agent.Plugin.Teams.Contracts;

public class TeamsMeetingUpdate
{
    public TeamsMeetingState? MeetingState { get; set; }

    public TeamsMeetingPermissions? MeetingPermissions { get; set; }
}
