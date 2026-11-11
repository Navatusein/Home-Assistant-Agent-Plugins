namespace HomeAssistant.Agent.Plugin.MacOS.Audio;

// Audio devices by the name macOS shows in Sound settings: only the ones that
// can be the default device, as Sound settings lists them.
internal static class AudioDevices
{
    public static IReadOnlyList<string> ListNames(AudioScope scope) =>
        GetSelectable(scope)
            .Select(CoreAudio.GetName)
            .OfType<string>()
            .Where(name => name.Length > 0)
            .Distinct()
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

    public static string? GetDefaultName(AudioScope scope)
    {
        var device = CoreAudio.GetDefaultDevice(scope);

        return device == 0 ? null : CoreAudio.GetName(device);
    }

    // Throws when the device is missing or macOS keeps the old default.
    public static void SetDefault(AudioScope scope, string deviceName)
    {
        var device = GetSelectable(scope).FirstOrDefault(d => CoreAudio.GetName(d) == deviceName);

        if (device == 0)
            throw new InvalidOperationException($"Audio device '{deviceName}' is not connected or cannot be the default.");

        if (!CoreAudio.SetDefaultDevice(scope, device) || CoreAudio.GetDefaultDevice(scope) != device)
            throw new InvalidOperationException($"macOS did not make '{deviceName}' the default device.");
    }

    private static IEnumerable<uint> GetSelectable(AudioScope scope) =>
        CoreAudio.GetDevices().Where(d => CoreAudio.HasStreams(d, scope) && CoreAudio.CanBeDefault(d, scope));
}
