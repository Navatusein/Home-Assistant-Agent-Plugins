namespace HomeAssistant.Agent.Plugin.Obs.Obs;

internal static class ObsOpCode
{
    public const int Hello = 0;
    public const int Identify = 1;
    public const int Identified = 2;
    public const int Event = 5;
    public const int Request = 6;
    public const int RequestResponse = 7;
}
