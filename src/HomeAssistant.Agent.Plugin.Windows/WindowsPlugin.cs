using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.Plugin.Base.Config;
using HomeAssistant.Agent.Plugin.Base.Interfaces;
using HomeAssistant.Agent.Plugin.Base.Tools;
using HomeAssistant.Agent.Plugin.Windows.Audio;
using HomeAssistant.Agent.Plugin.Windows.Config;
using HomeAssistant.Agent.Plugin.Windows.Features;
using HomeAssistant.Agent.Plugin.Windows.Keyboard;
using NAudio.CoreAudioApi;

namespace HomeAssistant.Agent.Plugin.Windows;

// The PC itself: power and session state from the Windows service, volume,
// audio devices, media, launchers, hotkeys and clipboard from the desktop app.
// Everything lives in the features (Features\); the plugin only turns the
// settings into one typed config they all read.
public class WindowsPlugin: BasePlugin
{
    public override string PluginCode => "windows-plugin";

    private const string OutputDeviceOptionsTool = "audio-output-device-options";
    private const string InputDeviceOptionsTool = "audio-input-device-options";

    public WindowsPlugin()
    {
        // Service features first: they run from boot, before anyone signs in.
        AddFeature(new PowerFeature());
        AddFeature(new PcRunningFeature());
        AddFeature(new SessionFeature());

        AddFeature(new LockFeature());
        AddFeature(new AgentRunningFeature());
        AddFeature(new VolumeFeature());
        AddFeature(new AudioDevicesFeature(OutputDeviceOptionsTool, InputDeviceOptionsTool));
        AddFeature(new MediaFeature());
        AddFeature(new LaunchersFeature());
        AddFeature(new HotkeysFeature());
        AddFeature(new ClipboardFeature());

        // Fill the device drop-downs of the Audio Devices settings.
        RegisterTool(new PluginTool
        {
            Name = OutputDeviceOptionsTool,
            Description = "Lists the names of the audio playback devices, as select options.",
            Handler = _ => Task.FromResult(PluginToolOptions.Serialize(AudioDevices.ListNames(DataFlow.Render)))
        });

        RegisterTool(new PluginTool
        {
            Name = InputDeviceOptionsTool,
            Description = "Lists the names of the audio recording devices, as select options.",
            Handler = _ => Task.FromResult(PluginToolOptions.Serialize(AudioDevices.ListNames(DataFlow.Capture)))
        });
    }

    // Always a new config: the current one is what the running features read,
    // so it is never modified in place. A list the form did not send (or sent
    // broken) keeps the current entries instead of being wiped.
    public override IPluginConfig CreateConfig(ConfigValues values)
    {
        var current = Config as WindowsPluginConfig;

        return new WindowsPluginConfig
        {
            ClipboardSlots = values.GetInt("ClipboardSlots", 0),
            ClipboardText = values.GetBool("ClipboardText", false),
            Applications = CleanApplications(values.GetList<WindowsApplication>("Applications"))
                ?? current?.Applications ?? [],
            Folders = CleanFolders(values.GetList<WindowsFolder>("Folders"))
                ?? current?.Folders ?? [],
            Hotkeys = ParseHotkeys(values.GetList<HotkeyFormEntry>("Hotkeys"))
                ?? current?.Hotkeys ?? [],
            OutputDevices = CleanDevices(values.GetList<AudioDevice>("OutputDevices"))
                ?? current?.OutputDevices ?? [],
            InputDevices = CleanDevices(values.GetList<AudioDevice>("InputDevices"))
                ?? current?.InputDevices ?? [],
        };
    }

    private static List<WindowsApplication>? CleanApplications(List<WindowsApplication>? applications)
    {
        if (applications == null)
            return null;

        applications.RemoveAll(a => string.IsNullOrWhiteSpace(a.Name) || string.IsNullOrWhiteSpace(a.Path));

        foreach (var application in applications)
        {
            if (string.IsNullOrWhiteSpace(application.Arguments))
                application.Arguments = null;
        }

        return applications;
    }

    private static List<WindowsFolder>? CleanFolders(List<WindowsFolder>? folders)
    {
        folders?.RemoveAll(f => string.IsNullOrWhiteSpace(f.Name) || string.IsNullOrWhiteSpace(f.Path));
        return folders;
    }

    // The optional fields have "unset = null" semantics downstream
    // (DisplayName fallback, shared communication endpoint), so empty inputs
    // must not survive as empty strings.
    private static List<AudioDevice>? CleanDevices(List<AudioDevice>? devices)
    {
        if (devices == null)
            return null;

        devices.RemoveAll(d => string.IsNullOrWhiteSpace(d.DeviceName));

        foreach (var device in devices)
        {
            if (string.IsNullOrWhiteSpace(device.CommunicationDeviceName))
                device.CommunicationDeviceName = null;

            if (string.IsNullOrWhiteSpace(device.FriendlyName))
                device.FriendlyName = null;
        }

        return devices;
    }

    // The form stores a hotkey's combination as one "+"-separated string
    // (e.g. "Ctrl+Shift+Esc"); the model keeps the keys as a list. Entries
    // with an unresolvable key are dropped as a whole — otherwise they would
    // become buttons that always fail.
    private static List<WindowsHotkey>? ParseHotkeys(List<HotkeyFormEntry>? entries)
    {
        if (entries == null)
            return null;

        var hotkeys = new List<WindowsHotkey>();

        foreach (var entry in entries)
        {
            var keys = (entry.Keys ?? string.Empty)
                .Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            if (string.IsNullOrWhiteSpace(entry.Name) || keys.Count == 0
                || !keys.All(k => VirtualKeys.TryResolve(k, out _, out _)))
                continue;

            hotkeys.Add(new WindowsHotkey { Name = entry.Name.Trim(), Keys = keys });
        }

        return hotkeys;
    }

    private sealed class HotkeyFormEntry
    {
        public string? Name { get; set; }
        public string? Keys { get; set; }
    }
}
