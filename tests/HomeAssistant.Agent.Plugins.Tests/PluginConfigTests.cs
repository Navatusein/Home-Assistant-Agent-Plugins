using System.Text.Json;
using System.Text.Json.Nodes;
using HomeAssistant.Agent.Plugin.Base.Config;
using HomeAssistant.Agent.Plugin.Logitech;
using HomeAssistant.Agent.Plugin.Logitech.Config;
using HomeAssistant.Agent.Plugin.Windows;
using HomeAssistant.Agent.Plugin.Windows.Config;

namespace HomeAssistant.Agent.Plugins.Tests;

// The values the settings form stores: strings, numbers, arrays of objects.
internal static class FormValues
{
    public static ConfigValues Of(params (string Key, object Value)[] values)
    {
        var json = new JsonObject();

        foreach (var (key, value) in values)
            json[key] = JsonSerializer.SerializeToNode(value);

        return new ConfigValues(json);
    }
}

public class WindowsPluginConfigTests
{
    [Fact]
    public void Drops_incomplete_entries_and_blank_optionals()
    {
        var config = (WindowsPluginConfig) new WindowsPlugin().CreateConfig(FormValues.Of(
            ("ClipboardSlots", 2),
            ("Applications", new object[]
            {
                new { Name = "Notes", Path = @"C:\notes.exe", Arguments = " " },
                new { Name = "", Path = @"C:\x.exe" },
                new { Name = "No path", Path = "" },
            }),
            ("OutputDevices", new object[]
            {
                new { DeviceName = "Speakers", CommunicationDeviceName = "", FriendlyName = "" },
                new { DeviceName = "" },
            })));

        Assert.Equal(2, config.ClipboardSlots);
        Assert.Equal("Notes", Assert.Single(config.Applications).Name);
        Assert.Null(config.Applications[0].Arguments);

        var device = Assert.Single(config.OutputDevices);
        Assert.Null(device.CommunicationDeviceName);
        Assert.Null(device.FriendlyName);
    }

    // A hotkey with a key that does not exist would become a button that
    // always fails — dropped as a whole.
    [Fact]
    public void Parses_hotkeys_and_drops_unknown_keys()
    {
        var config = (WindowsPluginConfig) new WindowsPlugin().CreateConfig(FormValues.Of(
            ("Hotkeys", new object[]
            {
                new { Name = "Task manager", Keys = "Ctrl + Shift + Esc" },
                new { Name = "Broken", Keys = "Ctrl+NoSuchKey" },
            })));

        var hotkey = Assert.Single(config.Hotkeys);
        Assert.Equal(["Ctrl", "Shift", "Esc"], hotkey.Keys);
    }

    // A list the form did not send keeps the current entries — and the
    // current config, which running features read, is never modified.
    [Fact]
    public async Task Keeps_untouched_lists_without_mutating_the_current_config()
    {
        var plugin = new WindowsPlugin();

        var first = (WindowsPluginConfig) plugin.CreateConfig(FormValues.Of(
            ("Applications", new object[] { new { Name = "Notes", Path = @"C:\notes.exe" } }),
            ("Folders", new object[] { new { Name = "Docs", Path = @"C:\docs" } })));

        await plugin.SetConfigAsync(first);

        var second = (WindowsPluginConfig) plugin.CreateConfig(FormValues.Of(
            ("Applications", Array.Empty<object>())));

        Assert.NotSame(first, second);
        Assert.Empty(second.Applications);
        Assert.Equal("Docs", Assert.Single(second.Folders).Name);
        Assert.Equal("Notes", Assert.Single(first.Applications).Name);
    }

    // Something other than a list under a list key is no list.
    [Fact]
    public void A_value_that_is_not_a_list_keeps_the_current_entries()
    {
        var config = (WindowsPluginConfig) new WindowsPlugin().CreateConfig(FormValues.Of(("Applications", "{ broken")));

        Assert.Empty(config.Applications);
    }
}

public class LogitechPluginConfigTests
{
    [Fact]
    public void Entries_that_do_not_fit_do_not_throw()
    {
        var config = (LogitechPluginConfig) new LogitechPlugin().CreateConfig(
            FormValues.Of(("Devices", new object[] { new { Device = new[] { 1 } } })));

        Assert.Empty(config.Devices);
    }

    [Fact]
    public void Drops_incomplete_devices()
    {
        var config = (LogitechPluginConfig) new LogitechPlugin().CreateConfig(FormValues.Of(
            ("Devices", new object[]
            {
                new { Device = "dev00000001", Name = "Mouse" },
                new { Device = "", Name = "Nothing" },
            })));

        Assert.Equal("Mouse", Assert.Single(config.Devices).Name);
    }
}
