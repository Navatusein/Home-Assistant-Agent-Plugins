namespace HomeAssistant.Agent.Plugin.MacOS.Config;

public class MacOsApplication
{
    public string Name { get; set; } = null!;

    // An .app bundle or an executable.
    public string Path { get; set; } = null!;

    public string? Arguments { get; set; }
}
