using System.Diagnostics;
using System.Text;

namespace HomeAssistant.Agent.Plugin.MacOS.Common;

// Runs the system tools (ioreg, shutdown, open, pmset). Arguments go one
// by one, so nothing is parsed by a shell.
internal static class Command
{
    public sealed record Result(int ExitCode, string Output, string Error);

    public static Result Run(string file, IEnumerable<string> arguments, TimeSpan timeout)
    {
        var info = CreateInfo(file, arguments);

        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.StandardOutputEncoding = Encoding.UTF8;
        info.StandardErrorEncoding = Encoding.UTF8;

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException($"{file} did not start.");

        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(timeout))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Exited meanwhile.
            }

            throw new TimeoutException($"{file} did not finish in {timeout.TotalSeconds:0} s.");
        }

        return new Result(process.ExitCode, output.Result, error.Result);
    }

    // Starts and does not wait.
    public static void Start(string file, params string[] arguments)
    {
        using var process = Process.Start(CreateInfo(file, arguments))
            ?? throw new InvalidOperationException($"{file} did not start.");
    }

    private static ProcessStartInfo CreateInfo(string file, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        return info;
    }
}
