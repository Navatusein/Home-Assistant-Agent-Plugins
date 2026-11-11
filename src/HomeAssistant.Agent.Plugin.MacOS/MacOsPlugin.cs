using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.Plugin.Base.Config;
using HomeAssistant.Agent.Plugin.Base.Interfaces;
using HomeAssistant.Agent.Plugin.Base.Tools;
using HomeAssistant.Agent.Plugin.MacOS.Audio;
using HomeAssistant.Agent.Plugin.MacOS.Config;
using HomeAssistant.Agent.Plugin.MacOS.Features;
using HomeAssistant.Agent.Plugin.MacOS.Keyboard;

namespace HomeAssistant.Agent.Plugin.MacOS;

// The Mac itself, the counterpart of the Windows plugin: power and session
// state from the background service (a LaunchDaemon), volume, audio devices,
// media, launchers, hotkeys and clipboard from the desktop app. Everything
// lives in the features (Features/); the plugin only turns the settings into
// one typed config they all read.
public class MacOsPlugin: BasePlugin
{
    public override string PluginCode => "macos-plugin";

    private const string OutputDeviceOptionsTool = "audio-output-device-options";
    private const string InputDeviceOptionsTool = "audio-input-device-options";

    public MacOsPlugin()
    {
        // Service features first: they run from boot, before anyone logs in.
        AddFeature(new PowerFeature());
        AddFeature(new ComputerRunningFeature());
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
            Description = "Lists the names of the audio output devices, as select options.",
            Handler = _ => Task.FromResult(PluginToolOptions.Serialize(AudioDevices.ListNames(AudioScope.Output)))
        });

        RegisterTool(new PluginTool
        {
            Name = InputDeviceOptionsTool,
            Description = "Lists the names of the audio input devices, as select options.",
            Handler = _ => Task.FromResult(PluginToolOptions.Serialize(AudioDevices.ListNames(AudioScope.Input)))
        });
    }

    // Always a new config: the current one is what the running features read,
    // so it is never modified in place. A list the form did not send (or sent
    // broken) keeps the current entries instead of being wiped.
    public override IPluginConfig CreateConfig(ConfigValues values)
    {
        var current = Config as MacOsPluginConfig;

        return new MacOsPluginConfig
        {
            ClipboardSlots = values.GetInt("ClipboardSlots", 0),
            ClipboardText = values.GetBool("ClipboardText", false),
            Applications = CleanApplications(values.GetList<MacOsApplication>("Applications"))
                ?? current?.Applications ?? [],
            Folders = CleanFolders(values.GetList<MacOsFolder>("Folders"))
                ?? current?.Folders ?? [],
            Hotkeys = ParseHotkeys(values.GetList<HotkeyFormEntry>("Hotkeys"))
                ?? current?.Hotkeys ?? [],
            OutputDevices = CleanDevices(values.GetList<AudioDevice>("OutputDevices"))
                ?? current?.OutputDevices ?? [],
            InputDevices = CleanDevices(values.GetList<AudioDevice>("InputDevices"))
                ?? current?.InputDevices ?? [],
        };
    }

    private static List<MacOsApplication>? CleanApplications(List<MacOsApplication>? applications)
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

    private static List<MacOsFolder>? CleanFolders(List<MacOsFolder>? folders)
    {
        folders?.RemoveAll(f => string.IsNullOrWhiteSpace(f.Name) || string.IsNullOrWhiteSpace(f.Path));
        return folders;
    }

    // FriendlyName has "unset = null" semantics downstream (DisplayName
    // fallback), so an empty input must not survive as an empty string.
    private static List<AudioDevice>? CleanDevices(List<AudioDevice>? devices)
    {
        if (devices == null)
            return null;

        devices.RemoveAll(d => string.IsNullOrWhiteSpace(d.DeviceName));

        foreach (var device in devices)
        {
            if (string.IsNullOrWhiteSpace(device.FriendlyName))
                device.FriendlyName = null;
        }

        return devices;
    }

    // The form stores a hotkey's combination as one "+"-separated string
    // (e.g. "Cmd+Option+Esc"); the model keeps the keys as a list. Entries
    // with an unresolvable key are dropped as a whole — otherwise they would
    // become buttons that always fail.
    private static List<MacOsHotkey>? ParseHotkeys(List<HotkeyFormEntry>? entries)
    {
        if (entries == null)
            return null;

        var hotkeys = new List<MacOsHotkey>();

        foreach (var entry in entries)
        {
            var keys = (entry.Keys ?? string.Empty)
                .Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            if (string.IsNullOrWhiteSpace(entry.Name) || keys.Count == 0
                || !keys.All(k => KeyCodes.TryResolve(k, out _, out _)))
                continue;

            hotkeys.Add(new MacOsHotkey { Name = entry.Name.Trim(), Keys = keys });
        }

        return hotkeys;
    }

    private sealed class HotkeyFormEntry
    {
        public string? Name { get; set; }
        public string? Keys { get; set; }
    }
}
