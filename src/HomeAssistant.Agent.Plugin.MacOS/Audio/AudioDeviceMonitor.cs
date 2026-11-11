using HomeAssistant.Agent.Plugin.MacOS.Common;
using Microsoft.Extensions.Logging;

namespace HomeAssistant.Agent.Plugin.MacOS.Audio;

// Watches the default output and input device, including changes made in
// macOS (Sound settings, the menu bar, plugging in headphones).
internal sealed class AudioDeviceMonitor : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly Poller _poller;

    private uint _output;
    private uint _input;

    // Arguments: scope and the new default device's name (null if it could
    // not be resolved).
    public event Action<AudioScope, string?>? DefaultDeviceChanged;

    public AudioDeviceMonitor(ILogger logger)
    {
        _output = CoreAudio.GetDefaultDevice(AudioScope.Output);
        _input = CoreAudio.GetDefaultDevice(AudioScope.Input);
        _poller = new Poller(PollInterval, Poll, logger);
    }

    public void Dispose()
    {
        _poller.Dispose();
    }

    private void Poll()
    {
        Check(AudioScope.Output, ref _output);
        Check(AudioScope.Input, ref _input);
    }

    private void Check(AudioScope scope, ref uint last)
    {
        var current = CoreAudio.GetDefaultDevice(scope);

        if (current == last)
            return;

        last = current;
        DefaultDeviceChanged?.Invoke(scope, current == 0 ? null : CoreAudio.GetName(current));
    }
}
