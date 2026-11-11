using HomeAssistant.Agent.Plugin.MacOS.Common;

namespace HomeAssistant.Agent.Plugin.MacOS.Launch;

internal static class ProcessLauncher
{
    // An .app bundle opens through Launch Services (`open -a`), like a double
    // click: an app already running comes to the front. Anything else is
    // started as a program.
    public static void LaunchApplication(string path, string? arguments)
    {
        if (path.EndsWith(".app", StringComparison.OrdinalIgnoreCase) || Directory.Exists(path))
        {
            string[] open = string.IsNullOrWhiteSpace(arguments)
                ? ["-a", path]
                : ["-a", path, "--args", ..SplitArguments(arguments)];

            Command.Start("/usr/bin/open", open);
            return;
        }

        Command.Start(path, [..SplitArguments(arguments ?? string.Empty)]);
    }

    // Shown in Finder.
    public static void OpenFolder(string path)
    {
        Command.Start("/usr/bin/open", path);
    }

    // The arguments as one would type them: separated by spaces, "quoted"
    // when they contain one.
    internal static IReadOnlyList<string> SplitArguments(string arguments)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        var hasArgument = false;

        foreach (var ch in arguments)
        {
            if (ch == '"')
            {
                quoted = !quoted;
                hasArgument = true;
            }
            else if (char.IsWhiteSpace(ch) && !quoted)
            {
                if (hasArgument)
                    result.Add(current.ToString());

                current.Clear();
                hasArgument = false;
            }
            else
            {
                current.Append(ch);
                hasArgument = true;
            }
        }

        if (hasArgument)
            result.Add(current.ToString());

        return result;
    }
}
