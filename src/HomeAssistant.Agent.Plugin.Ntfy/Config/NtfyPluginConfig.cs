using HomeAssistant.Agent.Plugin.Base.Interfaces;

namespace HomeAssistant.Agent.Plugin.Ntfy.Config;

// One row of the Topics list in the settings form.
public class NtfyTopicEntry
{
    public string? Topic { get; set; }
}

public class NtfyPluginConfig : IPluginConfig
{
    public const string DefaultServer = "https://ntfy.sh";

    // Without a trailing slash: https://ntfy.sh, https://ntfy.example.com/ntfy.
    public string Server { get; set; } = DefaultServer;

    public IReadOnlyList<string> Topics { get; set; } = [];

    // Empty for topics that need no access token.
    public string Token { get; set; } = string.Empty;

    // Messages below it are not shown: 1 (min) … 5 (max), ntfy's default is 3.
    public int MinPriority { get; set; } = 1;

    // Whether "http" buttons are shown: a click sends the request the message
    // describes, from this computer — whoever can publish to the topic
    // chooses it.
    public bool HttpActions { get; set; }
}
