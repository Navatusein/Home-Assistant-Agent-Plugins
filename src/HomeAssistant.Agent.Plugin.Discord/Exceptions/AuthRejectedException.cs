namespace HomeAssistant.Agent.Plugin.Discord.Exceptions;

public class AuthRejectedException : Exception 
{
    public AuthRejectedException(string message) : base(message) {}
}