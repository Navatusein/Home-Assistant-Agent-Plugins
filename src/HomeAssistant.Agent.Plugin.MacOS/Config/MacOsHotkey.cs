namespace HomeAssistant.Agent.Plugin.MacOS.Config;

public class MacOsHotkey
{
    public string Name { get; set; } = null!;

    // Keys pressed together, in press order. Modifiers first, e.g.
    // ["Cmd", "Option", "Esc"]. See KeyCodes for supported names.
    public List<string> Keys { get; set; } = [];
}
