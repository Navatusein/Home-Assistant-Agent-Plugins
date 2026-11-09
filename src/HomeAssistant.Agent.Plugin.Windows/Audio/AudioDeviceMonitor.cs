using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace HomeAssistant.Agent.Plugin.Windows.Audio;

internal sealed class AudioDeviceMonitor: IMMNotificationClient, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator;

    // Raised when the default endpoint changes (including changes made outside
    // this app). Arguments: data flow and the new default device's Windows
    // friendly name (null if it could not be resolved).
    public event Action<DataFlow, string?>? DefaultDeviceChanged;

    public AudioDeviceMonitor()
    {
        _enumerator = new MMDeviceEnumerator();
        _enumerator.RegisterEndpointNotificationCallback(this);
    }

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        // Only react once per default change; we set all roles when switching.
        if (role != Role.Multimedia)
            return;

        DefaultDeviceChanged?.Invoke(flow, ResolveFriendlyName(defaultDeviceId));
    }

    private string? ResolveFriendlyName(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
            return null;

        try
        {
            using var device = _enumerator.GetDevice(deviceId);
            return device.FriendlyName;
        }
        catch
        {
            return null;
        }
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
    public void OnDeviceAdded(string pwstrDeviceId) { }
    public void OnDeviceRemoved(string deviceId) { }
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    public void Dispose()
    {
        _enumerator.UnregisterEndpointNotificationCallback(this);
        _enumerator.Dispose();
    }
}
