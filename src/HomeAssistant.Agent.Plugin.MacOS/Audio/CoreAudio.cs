using System.Runtime.InteropServices;
using HomeAssistant.Agent.Plugin.MacOS.Native;

namespace HomeAssistant.Agent.Plugin.MacOS.Audio;

internal enum AudioScope
{
    Output,
    Input
}

// The CoreAudio HAL: devices, the default devices, volume and mute. Every
// call is a plain property read or write — cheap enough to poll.
internal static unsafe class CoreAudio
{
    private const string Lib = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";

    private const uint SystemObject = 1;
    private const uint ElementMain = 0;

    private static readonly uint ScopeGlobal = FourCc("glob");
    private static readonly uint ScopeInput = FourCc("inpt");
    private static readonly uint ScopeOutput = FourCc("outp");

    private static readonly uint PropertyDevices = FourCc("dev#");
    private static readonly uint PropertyDefaultOutputDevice = FourCc("dOut");
    private static readonly uint PropertyDefaultInputDevice = FourCc("dIn ");
    private static readonly uint PropertyName = FourCc("lnam");
    private static readonly uint PropertyStreams = FourCc("stm#");
    private static readonly uint PropertyCanBeDefault = FourCc("dflt");
    private static readonly uint PropertyVolumeScalar = FourCc("volm");
    private static readonly uint PropertyMute = FourCc("mute");

    // kAudioHardwareServiceDeviceProperty_VirtualMainVolume: the volume the
    // menu bar slider shows, also for devices that only have per-channel
    // volume. Not every device has it, hence the fallbacks.
    private static readonly uint PropertyVirtualMainVolume = FourCc("vmvc");

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyAddress
    {
        public uint Selector;
        public uint Scope;
        public uint Element;
    }

    [DllImport(Lib)]
    private static extern int AudioObjectGetPropertyDataSize(uint objectId, in PropertyAddress address,
        uint qualifierSize, IntPtr qualifier, out uint dataSize);

    [DllImport(Lib)]
    private static extern int AudioObjectGetPropertyData(uint objectId, in PropertyAddress address,
        uint qualifierSize, IntPtr qualifier, ref uint dataSize, void* data);

    [DllImport(Lib)]
    private static extern int AudioObjectSetPropertyData(uint objectId, in PropertyAddress address,
        uint qualifierSize, IntPtr qualifier, uint dataSize, void* data);

    [DllImport(Lib)]
    private static extern byte AudioObjectHasProperty(uint objectId, in PropertyAddress address);

    [DllImport(Lib)]
    private static extern int AudioObjectIsPropertySettable(uint objectId, in PropertyAddress address,
        out byte isSettable);

    public static uint[] GetDevices()
    {
        var address = Address(PropertyDevices, ScopeGlobal);

        if (AudioObjectGetPropertyDataSize(SystemObject, in address, 0, IntPtr.Zero, out var size) != 0 || size == 0)
            return [];

        var devices = new uint[size / sizeof(uint)];

        fixed (uint* data = devices)
        {
            if (AudioObjectGetPropertyData(SystemObject, in address, 0, IntPtr.Zero, ref size, data) != 0)
                return [];
        }

        return devices[..(int) (size / sizeof(uint))];
    }

    public static string? GetName(uint device)
    {
        var address = Address(PropertyName, ScopeGlobal);
        var name = IntPtr.Zero;
        var size = (uint) sizeof(IntPtr);

        if (AudioObjectGetPropertyData(device, in address, 0, IntPtr.Zero, ref size, &name) != 0 || name == IntPtr.Zero)
            return null;

        try
        {
            return CoreFoundation.GetString(name);
        }
        finally
        {
            CoreFoundation.CFRelease(name);
        }
    }

    // Whether the device plays (Output) or records (Input) at all.
    public static bool HasStreams(uint device, AudioScope scope)
    {
        var address = Address(PropertyStreams, Scope(scope));

        return AudioObjectGetPropertyDataSize(device, in address, 0, IntPtr.Zero, out var size) == 0 && size > 0;
    }

    // Whether macOS lets the device be the default output (or input). Some
    // virtual devices (e.g. Microsoft Teams Audio) have streams but refuse:
    // setting them reports success and changes nothing.
    public static bool CanBeDefault(uint device, AudioScope scope)
    {
        var address = Address(PropertyCanBeDefault, Scope(scope));
        uint canBeDefault = 0;
        var size = (uint) sizeof(uint);

        return AudioObjectGetPropertyData(device, in address, 0, IntPtr.Zero, ref size, &canBeDefault) == 0
            && canBeDefault != 0;
    }

    // 0 when there is none.
    public static uint GetDefaultDevice(AudioScope scope)
    {
        var address = Address(DefaultDeviceProperty(scope), ScopeGlobal);
        uint device = 0;
        var size = (uint) sizeof(uint);

        return AudioObjectGetPropertyData(SystemObject, in address, 0, IntPtr.Zero, ref size, &device) == 0
            ? device
            : 0;
    }

    public static bool SetDefaultDevice(AudioScope scope, uint device)
    {
        var address = Address(DefaultDeviceProperty(scope), ScopeGlobal);

        return AudioObjectSetPropertyData(SystemObject, in address, 0, IntPtr.Zero, sizeof(uint), &device) == 0;
    }

    // 0..1 for the output; null when the device has no volume control (some
    // HDMI and digital outputs).
    public static double? GetVolume(uint device)
    {
        if (device == 0)
            return null;

        if (TryGetFloat(device, PropertyVirtualMainVolume, ElementMain, out var value)
            || TryGetFloat(device, PropertyVolumeScalar, ElementMain, out value))
            return value;

        var channels = new List<double>(2);

        for (uint channel = 1; channel <= 2; channel++)
        {
            if (TryGetFloat(device, PropertyVolumeScalar, channel, out value))
                channels.Add(value);
        }

        return channels.Count > 0 ? channels.Average() : null;
    }

    public static void SetVolume(uint device, double scalar)
    {
        if (device == 0)
            return;

        var value = (float) Math.Clamp(scalar, 0.0, 1.0);

        if (TrySetFloat(device, PropertyVirtualMainVolume, ElementMain, value)
            || TrySetFloat(device, PropertyVolumeScalar, ElementMain, value))
            return;

        for (uint channel = 1; channel <= 2; channel++)
            TrySetFloat(device, PropertyVolumeScalar, channel, value);
    }

    public static bool GetMute(uint device)
    {
        var address = Address(PropertyMute, ScopeOutput);
        uint mute = 0;
        var size = (uint) sizeof(uint);

        return device != 0
            && AudioObjectHasProperty(device, in address) != 0
            && AudioObjectGetPropertyData(device, in address, 0, IntPtr.Zero, ref size, &mute) == 0
            && mute != 0;
    }

    public static void SetMute(uint device, bool mute)
    {
        var address = Address(PropertyMute, ScopeOutput);
        var value = mute ? 1u : 0u;

        if (device != 0 && IsSettable(device, address))
            AudioObjectSetPropertyData(device, in address, 0, IntPtr.Zero, sizeof(uint), &value);
    }

    private static bool TryGetFloat(uint device, uint selector, uint element, out double value)
    {
        var address = Address(selector, ScopeOutput, element);
        float result = 0;
        var size = (uint) sizeof(float);

        value = 0;

        if (AudioObjectHasProperty(device, in address) == 0
            || AudioObjectGetPropertyData(device, in address, 0, IntPtr.Zero, ref size, &result) != 0)
            return false;

        value = result;
        return true;
    }

    private static bool TrySetFloat(uint device, uint selector, uint element, float value)
    {
        var address = Address(selector, ScopeOutput, element);

        return IsSettable(device, address)
            && AudioObjectSetPropertyData(device, in address, 0, IntPtr.Zero, sizeof(float), &value) == 0;
    }

    private static bool IsSettable(uint device, PropertyAddress address) =>
        AudioObjectHasProperty(device, in address) != 0
        && AudioObjectIsPropertySettable(device, in address, out var settable) == 0
        && settable != 0;

    private static uint DefaultDeviceProperty(AudioScope scope) =>
        scope == AudioScope.Output ? PropertyDefaultOutputDevice : PropertyDefaultInputDevice;

    private static uint Scope(AudioScope scope) => scope == AudioScope.Output ? ScopeOutput : ScopeInput;

    private static PropertyAddress Address(uint selector, uint scope, uint element = ElementMain) =>
        new() { Selector = selector, Scope = scope, Element = element };

    private static uint FourCc(string code) =>
        (uint) code[0] << 24 | (uint) code[1] << 16 | (uint) code[2] << 8 | code[3];
}
