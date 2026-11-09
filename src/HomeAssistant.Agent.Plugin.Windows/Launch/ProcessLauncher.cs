using System.Diagnostics;

namespace HomeAssistant.Agent.Plugin.Windows.Launch;

internal static class ProcessLauncher
{
    public static void LaunchApplication(string path, string? arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = path,
            Arguments = arguments ?? string.Empty,
            UseShellExecute = true
        };

        Process.Start(startInfo);
    }

    public static void OpenFolder(string path)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true
        };

        Process.Start(startInfo);
    }
}
