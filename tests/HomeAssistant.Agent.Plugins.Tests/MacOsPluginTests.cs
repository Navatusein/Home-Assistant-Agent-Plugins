using HomeAssistant.Agent.Plugin.MacOS;
using HomeAssistant.Agent.Plugin.MacOS.Config;
using HomeAssistant.Agent.Plugin.MacOS.Launch;
using HomeAssistant.Agent.Plugin.MacOS.Session;

// The plugin is marked macOS-only; these tests touch only its pure parts
// (config, parsing), which run anywhere.
#pragma warning disable CA1416

namespace HomeAssistant.Agent.Plugins.Tests;

public class MacOsPluginConfigTests
{
    [Fact]
    public void Drops_incomplete_entries_and_blank_optionals()
    {
        var config = (MacOsPluginConfig) new MacOsPlugin().CreateConfig(FormValues.Of(
            ("ClipboardSlots", 2),
            ("Applications", new object[]
            {
                new { Name = "Notes", Path = "/System/Applications/Notes.app", Arguments = " " },
                new { Name = "", Path = "/Applications/X.app" },
                new { Name = "No path", Path = "" },
            }),
            ("OutputDevices", new object[]
            {
                new { DeviceName = "MacBook Pro Speakers", FriendlyName = "" },
                new { DeviceName = "" },
            })));

        Assert.Equal(2, config.ClipboardSlots);
        Assert.Equal("Notes", Assert.Single(config.Applications).Name);
        Assert.Null(config.Applications[0].Arguments);
        Assert.Null(Assert.Single(config.OutputDevices).FriendlyName);
    }

    [Fact]
    public void Parses_hotkeys_and_drops_unknown_keys()
    {
        var config = (MacOsPluginConfig) new MacOsPlugin().CreateConfig(FormValues.Of(
            ("Hotkeys", new object[]
            {
                new { Name = "Force quit", Keys = "Cmd + Option + Esc" },
                new { Name = "Broken", Keys = "Cmd+NoSuchKey" },
            })));

        var hotkey = Assert.Single(config.Hotkeys);
        Assert.Equal(["Cmd", "Option", "Esc"], hotkey.Keys);
    }
}

public class MacOsSessionTests
{
    private const string Header =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        """;

    private static string Plist(string users) =>
        $"""
        {Header}
        <plist version="1.0">
        <dict>
          <key>IOConsoleLocked</key>
          <false/>
          <key>IOConsoleUsers</key>
          <array>{users}</array>
          <key>IORegistryEntryName</key>
          <string>Root</string>
        </dict>
        </plist>
        """;

    private static string User(bool onConsole, bool loginDone, bool? locked) =>
        $"""
        <dict>
          <key>kCGSSessionOnConsoleKey</key>
          <{(onConsole ? "true" : "false")}/>
          <key>kCGSessionLoginDoneKey</key>
          <{(loginDone ? "true" : "false")}/>
          <key>kCGSSessionUserNameKey</key>
          <string>user</string>
          {(locked == null ? "" : $"<key>CGSSessionScreenIsLocked</key><{(locked.Value ? "true" : "false")}/>")}
        </dict>
        """;

    [Fact]
    public void A_user_on_the_console_without_the_lock_flag_is_unlocked()
    {
        Assert.True(SessionMonitor.ParseSessionUnlocked(Plist(User(true, true, null))));
    }

    [Fact]
    public void A_locked_screen_is_locked()
    {
        Assert.False(SessionMonitor.ParseSessionUnlocked(Plist(User(true, true, true))));
    }

    // Fast user switching: the other user is logged in but not on screen.
    [Fact]
    public void Only_the_user_on_the_console_counts()
    {
        Assert.False(SessionMonitor.ParseSessionUnlocked(Plist(User(false, true, null))));
        Assert.True(SessionMonitor.ParseSessionUnlocked(Plist(User(false, true, null) + User(true, true, false))));
    }

    [Fact]
    public void The_login_window_is_locked()
    {
        Assert.False(SessionMonitor.ParseSessionUnlocked(Plist(string.Empty)));
        Assert.False(SessionMonitor.ParseSessionUnlocked(Plist(User(true, false, null))));
    }
}

public class MacOsLauncherTests
{
    [Fact]
    public void Splits_arguments_and_keeps_quoted_spaces()
    {
        Assert.Equal(["--new-window", "/Users/me/My Files", ""],
            ProcessLauncher.SplitArguments("""  --new-window "/Users/me/My Files" "" """));
    }
}
