namespace HomeAssistant.Agent.Plugin.Windows.Config;

public class WindowsHotkey
{
    public string Name { get; set; } = null!;

    // Keys pressed together, in press order. Modifiers first, e.g.
    // ["Ctrl", "Shift", "Esc"]. See VirtualKeys for supported names.
    public List<string> Keys { get; set; } = [];
}
