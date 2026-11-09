using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HomeAssistant.Agent.Plugin.Windows.Power;

internal static class PowerController
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();

    public static void Shutdown()
    {
        RunShutdown("/s /t 0");
    }

    public static void Restart()
    {
        RunShutdown("/r /t 0");
    }

    public static void Lock()
    {
        if (!LockWorkStation())
            throw new InvalidOperationException("Failed to lock the workstation.");
    }

    private static void RunShutdown(string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "shutdown.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process.Start(startInfo);
    }
}
