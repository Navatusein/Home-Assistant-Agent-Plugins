using HomeAssistant.Agent.Plugin.MacOS.Common;
using Microsoft.Extensions.Logging;

namespace HomeAssistant.Agent.Plugin.MacOS.Audio;

// Volume and mute of the default output device. Polled: the values follow
// the device, so a change of the default device is picked up on its own.
internal sealed class SystemVolumeController : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly Poller _poller;

    private (double Percent, bool Mute) _last;

    // Raised when volume or mute changes, including changes made outside this
    // app. Arguments: volume percent (0..100), muted.
    public event Action<double, bool>? Changed;

    public SystemVolumeController(ILogger logger)
    {
        _last = Read();
        _poller = new Poller(PollInterval, Poll, logger);
    }

    public double GetVolumePercent() => _last.Percent;

    public bool GetMute() => _last.Mute;

    public void SetVolumePercent(double percent)
    {
        CoreAudio.SetVolume(CoreAudio.GetDefaultDevice(AudioScope.Output), Math.Clamp(percent, 0.0, 100.0) / 100.0);
    }

    public void SetMute(bool mute)
    {
        CoreAudio.SetMute(CoreAudio.GetDefaultDevice(AudioScope.Output), mute);
    }

    public void Dispose()
    {
        _poller.Dispose();
    }

    private static (double Percent, bool Mute) Read()
    {
        var device = CoreAudio.GetDefaultDevice(AudioScope.Output);
        var volume = CoreAudio.GetVolume(device) ?? 0;

        return (Math.Round(volume * 100.0), CoreAudio.GetMute(device));
    }

    private void Poll()
    {
        var current = Read();

        if (current == _last)
            return;

        _last = current;
        Changed?.Invoke(current.Percent, current.Mute);
    }
}
