namespace HomeAssistant.Agent.Plugin.Windows.Config;

public class WindowsApplication
{
    public string Name { get; set; } = null!;

    public string Path { get; set; } = null!;

    public string? Arguments { get; set; }
}
