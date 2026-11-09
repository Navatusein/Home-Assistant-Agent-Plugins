using NAudio.CoreAudioApi;

namespace HomeAssistant.Agent.Plugin.Windows.Audio;

internal sealed class SystemVolumeController: IDisposable
{
    private readonly MMDeviceEnumerator _enumerator;
    private readonly MMDevice _device;

    // Raised when volume or mute changes, including changes made outside this app.
    // Arguments: volume percent (0..100), muted.
    public event Action<double, bool>? Changed;

    public SystemVolumeController()
    {
        _enumerator = new MMDeviceEnumerator();
        _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        _device.AudioEndpointVolume.OnVolumeNotification += OnVolumeNotification;
    }

    public double GetVolumePercent()
    {
        return _device.AudioEndpointVolume.MasterVolumeLevelScalar * 100.0;
    }

    public void SetVolumePercent(double percent)
    {
        _device.AudioEndpointVolume.MasterVolumeLevelScalar = (float) (Math.Clamp(percent, 0.0, 100.0) / 100.0);
    }

    public bool GetMute()
    {
        return _device.AudioEndpointVolume.Mute;
    }

    public void SetMute(bool mute)
    {
        _device.AudioEndpointVolume.Mute = mute;
    }

    private void OnVolumeNotification(AudioVolumeNotificationData data)
    {
        Changed?.Invoke(data.MasterVolume * 100.0, data.Muted);
    }

    public void Dispose()
    {
        _device.AudioEndpointVolume.OnVolumeNotification -= OnVolumeNotification;
        _device.Dispose();
        _enumerator.Dispose();
    }
}
