namespace HomeAssistant.Agent.Plugin.Logitech.Contracts;

// Envelope used by the G HUB agent in both directions.
internal sealed class GHubMessage
{
    public string? Path { get; set; }

    public string? Verb { get; set; }

    public GHubPayload? Payload { get; set; }
}
