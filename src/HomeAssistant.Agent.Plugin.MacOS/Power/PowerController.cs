using System.Runtime.InteropServices;
using HomeAssistant.Agent.Plugin.MacOS.Common;

namespace HomeAssistant.Agent.Plugin.MacOS.Power;

internal static class PowerController
{
    // The private framework behind the menu bar's "Lock Screen".
    private const string LoginFramework = "/System/Library/PrivateFrameworks/login.framework/Versions/Current/login";

    // Run by the service, which is root: shutdown needs it.
    public static void Shutdown()
    {
        Command.Start("/sbin/shutdown", "-h", "now");
    }

    public static void Restart()
    {
        Command.Start("/sbin/shutdown", "-r", "now");
    }

    public static void Lock()
    {
        if (TryLockScreen())
            return;

        // Fallback: the display goes to sleep, which locks the Mac when
        // "Require password" is set to immediately.
        Command.Start("/usr/bin/pmset", "displaysleepnow");
    }

    private static unsafe bool TryLockScreen()
    {
        if (!NativeLibrary.TryLoad(LoginFramework, out var library)
            || !NativeLibrary.TryGetExport(library, "SACLockScreenImmediate", out var lockScreen))
            return false;

        ((delegate* unmanaged<int>) lockScreen)();
        return true;
    }
}
