using System.Text;
using NAudio.CoreAudioApi;

namespace HomeAssistant.Agent.Plugin.Windows.Audio;

internal static class AudioDevices
{
    // Names of the endpoints in the given direction, as shown in the Windows
    // sound settings — the same string the config matches devices by.
    // Only active devices: offering an unplugged one as a fresh choice would
    // just create a setting that cannot work.
    public static IReadOnlyList<string> ListNames(DataFlow flow)
    {
        using var enumerator = new MMDeviceEnumerator();

        var names = new List<string>();

        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            using (device)
                names.Add(device.FriendlyName);
        }

        names.Sort(StringComparer.CurrentCultureIgnoreCase);

        return names;
    }

    public static string Describe()
    {
        var builder = new StringBuilder();

        AppendFlow(builder, "Output (Render)", DataFlow.Render);
        builder.AppendLine();
        AppendFlow(builder, "Input (Capture)", DataFlow.Capture);

        return builder.ToString();
    }

    private static void AppendFlow(StringBuilder builder, string title, DataFlow flow)
    {
        builder.AppendLine($"=== {title} ===");

        using var enumerator = new MMDeviceEnumerator();

        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.All))
        {
            using (device)
                builder.AppendLine($"[{device.State}] {device.FriendlyName}");
        }
    }
}
