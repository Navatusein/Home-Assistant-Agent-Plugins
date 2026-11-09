namespace HomeAssistant.Agent.Plugin.Teams.Contracts;

public class TeamsMeetingState
{
    public bool IsMuted { get; set; }

    public bool IsVideoOn { get; set; }

    public bool IsInMeeting { get; set; }
}
