using HomeAssistant.Agent.Plugin.Base.Interfaces;

namespace HomeAssistant.Agent.Plugin.Windows.Config;

public class WindowsPluginConfig: IPluginConfig
{
    public List<WindowsApplication> Applications { get; set; } = [];

    public List<WindowsFolder> Folders { get; set; } = [];

    // Key combinations exposed as buttons. Pressing a button sends the
    // combination to the currently focused window.
    public List<WindowsHotkey> Hotkeys { get; set; } = [];

    // Output/input devices to expose. Defined up front so the options stay
    // stable even when a device is currently disconnected.
    public List<AudioDevice> OutputDevices { get; set; } = [];

    public List<AudioDevice> InputDevices { get; set; } = [];

    // Number of clipboard slots to expose. Each slot gets a save/paste button,
    // a clear button and a sensor showing its stored content.
    public int ClipboardSlots { get; set; }

    // A notify entity that puts the text sent to it on the clipboard.
    public bool ClipboardText { get; set; }
}
