using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace HomeAssistant.Agent.Plugin.Windows.Audio;

internal sealed class AudioDeviceSwitcher
{
    public bool TrySetDefaultDevice(DataFlow flow, string friendlyName)
    {
        return SetRoles(flow, friendlyName, ERole.eConsole, ERole.eMultimedia, ERole.eCommunications);
    }

    // Sets the default playback device, optionally routing the "communication"
    // role to a separate device. When communicationName is null the sound
    // device is used for communication as well.
    public bool TrySetDefaultPlaybackDevice(string soundName, string? communicationName)
    {
        var soundSet = SetRoles(DataFlow.Render, soundName, ERole.eConsole, ERole.eMultimedia);

        if (string.IsNullOrWhiteSpace(communicationName))
            SetRoles(DataFlow.Render, soundName, ERole.eCommunications);
        else
            SetRoles(DataFlow.Render, communicationName, ERole.eCommunications);

        return soundSet;
    }

    private bool SetRoles(DataFlow flow, string friendlyName, params ERole[] roles)
    {
        var deviceId = FindDeviceId(flow, friendlyName);

        if (deviceId == null)
            return false;

        var policyConfig = (IPolicyConfig) new PolicyConfigClient();

        try
        {
            foreach (var role in roles)
                policyConfig.SetDefaultEndpoint(deviceId, role);

            return true;
        }
        finally
        {
            Marshal.ReleaseComObject(policyConfig);
        }
    }

    public string? GetDefaultDeviceFriendlyName(DataFlow flow)
    {
        using var enumerator = new MMDeviceEnumerator();

        if (!enumerator.HasDefaultAudioEndpoint(flow, Role.Multimedia))
            return null;

        using var device = enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
        return device.FriendlyName;
    }

    private static string? FindDeviceId(DataFlow flow, string friendlyName)
    {
        using var enumerator = new MMDeviceEnumerator();

        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            using (device)
            {
                if (device.FriendlyName == friendlyName)
                    return device.ID;
            }
        }

        return null;
    }
}

internal enum ERole
{
    eConsole = 0,
    eMultimedia = 1,
    eCommunications = 2
}

[ComImport]
[Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
internal class PolicyConfigClient
{
}

[ComImport]
[Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    [PreserveSig] int GetMixFormat(string deviceName, IntPtr format);
    [PreserveSig] int GetDeviceFormat(string deviceName, bool @default, IntPtr format);
    [PreserveSig] int ResetDeviceFormat(string deviceName);
    [PreserveSig] int SetDeviceFormat(string deviceName, IntPtr endpointFormat, IntPtr mixFormat);
    [PreserveSig] int GetProcessingPeriod(string deviceName, bool @default, IntPtr defaultPeriod, IntPtr minimumPeriod);
    [PreserveSig] int SetProcessingPeriod(string deviceName, IntPtr period);
    [PreserveSig] int GetShareMode(string deviceName, IntPtr mode);
    [PreserveSig] int SetShareMode(string deviceName, IntPtr mode);
    [PreserveSig] int GetPropertyValue(string deviceName, bool fxStore, IntPtr key, IntPtr value);
    [PreserveSig] int SetPropertyValue(string deviceName, bool fxStore, IntPtr key, IntPtr value);
    [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
    [PreserveSig] int SetEndpointVisibility(string deviceName, bool visible);
}
