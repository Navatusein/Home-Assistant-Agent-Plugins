using System.Runtime.InteropServices;

namespace HomeAssistant.Agent.Plugin.Sendspin.Audio;

// The few Core Audio interfaces the player needs, declared here rather than
// taken from NAudio: NAudio creates the device enumerator as a [ComImport]
// class, and the runtime ties such a class to the load context that created
// it first — with the Windows plugin's NAudio already loaded, ours failed with
// "cannot cast MMDeviceEnumeratorComObject to MMDeviceEnumeratorComObject".
// Objects created through CoCreateInstance and used through interfaces do not
// have that problem.
internal static class Wasapi
{
    private static readonly Guid MMDeviceEnumeratorClass = new("BCDE0395-E52F-467C-8E3D-C4579291692E");

    public static readonly Guid AudioClientInterface = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    public static readonly Guid AudioRenderClientInterface = new("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");

    public const int ClsCtxAll = 0x17;

    public const int StreamFlagsEventCallback = 0x00040000;
    public const int StreamFlagsAutoConvertPcm = unchecked((int)0x80000000);
    public const int StreamFlagsSrcDefaultQuality = 0x08000000;

    // In 100 ns units, as Core Audio counts.
    public const long TicksPerMillisecond = 10_000;

    // The device the stream plays on was unplugged, disabled or reconfigured.
    public const int DeviceInvalidated = unchecked((int)0x88890004);

    private static readonly PropertyKey FriendlyNameKey = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

    public static IMMDeviceEnumerator CreateDeviceEnumerator()
    {
        var iid = typeof(IMMDeviceEnumerator).GUID;
        Marshal.ThrowExceptionForHR(CoCreateInstance(MMDeviceEnumeratorClass, IntPtr.Zero, ClsCtxAll, iid, out var enumerator));
        return (IMMDeviceEnumerator)enumerator;
    }

    // "Speakers (Realtek USB Audio)", or the id when the name cannot be read.
    public static string GetFriendlyName(IMMDevice device)
    {
        device.GetId(out var id);

        try
        {
            device.OpenPropertyStore(0 /* STGM_READ */, out var properties);

            var key = FriendlyNameKey;
            properties.GetValue(ref key, out var value);

            try
            {
                return value.Type == 31 /* VT_LPWSTR */ ? Marshal.PtrToStringUni(value.Pointer) ?? id : id;
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
        catch (COMException)
        {
            return id;
        }
    }

    // 32-bit float PCM, interleaved: what the SDK's sample source produces.
    public static IntPtr AllocFloatFormat(int sampleRate, int channels)
    {
        var format = new WaveFormatEx
        {
            FormatTag = 3, // WAVE_FORMAT_IEEE_FLOAT
            Channels = (ushort)channels,
            SamplesPerSec = (uint)sampleRate,
            BitsPerSample = 32,
            BlockAlign = (ushort)(channels * sizeof(float)),
            AvgBytesPerSec = (uint)(sampleRate * channels * sizeof(float)),
        };

        var pointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WaveFormatEx>());
        Marshal.StructureToPtr(format, pointer, false);
        return pointer;
    }

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(in Guid clsid, IntPtr outer, int context, in Guid iid,
        [MarshalAs(UnmanagedType.IUnknown)] out object instance);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormatEx
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSec;
        public uint AvgBytesPerSec;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort Size;
    }
}

internal enum EDataFlow
{
    Render = 0,
}

internal enum ERole
{
    Console = 0,
    Multimedia = 1,
    Communications = 2,
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey(Guid formatId, int propertyId)
{
    public Guid FormatId = formatId;
    public int PropertyId = propertyId;
}

// Enough of PROPVARIANT for a string value: the type, then the pointer.
[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    public ushort Type;
    private ushort _reserved1;
    private ushort _reserved2;
    private ushort _reserved3;
    public IntPtr Pointer;
    private IntPtr _padding;
}

internal enum AudioClientShareMode
{
    Shared = 0,
}

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    void EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IntPtr devices);

    void GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);

    void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

    void RegisterEndpointNotificationCallback(IMMNotificationClient client);

    void UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

// Implemented by the player: Windows calls it on a thread of its own.
[ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMNotificationClient
{
    void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int newState);

    void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    void OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string? defaultDeviceId);

    void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PropertyKey key);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    void Activate(in Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);

    void OpenPropertyStore(int access, out IPropertyStore properties);

    void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
}

[ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient
{
    void Initialize(AudioClientShareMode shareMode, int streamFlags, long bufferDuration, long periodicity,
        IntPtr format, IntPtr sessionGuid);

    void GetBufferSize(out uint frames);

    void GetStreamLatency(out long latency);

    void GetCurrentPadding(out uint frames);

    [PreserveSig]
    int IsFormatSupported(AudioClientShareMode shareMode, IntPtr format, out IntPtr closestMatch);

    void GetMixFormat(out IntPtr format);

    void GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);

    void Start();

    void Stop();

    void Reset();

    void SetEventHandle(IntPtr eventHandle);

    void GetService(in Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
}

[ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioRenderClient
{
    void GetBuffer(uint frames, out IntPtr data);

    void ReleaseBuffer(uint frames, int flags);
}

[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    void GetCount(out int count);

    void GetAt(int index, out PropertyKey key);

    void GetValue(ref PropertyKey key, out PropVariant value);
}
