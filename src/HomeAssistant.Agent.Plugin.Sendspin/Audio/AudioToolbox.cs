using System.Runtime.InteropServices;

namespace HomeAssistant.Agent.Plugin.Sendspin.Audio;

// The part of AudioToolbox's Audio Queue Services the macOS player needs: an
// output queue on the default device, fed buffer by buffer from a callback.
internal static class AudioToolbox
{
    private const string Lib = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";

    private const uint FormatLinearPcm = 0x6C70636D; // 'lpcm'
    private const uint FormatFlagIsFloat = 1;
    private const uint FormatFlagIsPacked = 8;

    // Called on the queue's own thread each time the device is done with a
    // buffer, for it to be filled and enqueued again.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OutputCallback(IntPtr userData, IntPtr queue, IntPtr buffer);

    [StructLayout(LayoutKind.Sequential)]
    public struct StreamDescription
    {
        public double SampleRate;
        public uint FormatId;
        public uint FormatFlags;
        public uint BytesPerPacket;
        public uint FramesPerPacket;
        public uint BytesPerFrame;
        public uint ChannelsPerFrame;
        public uint BitsPerChannel;
        public uint Reserved;
    }

    // The head of AudioQueueBuffer: capacity, data, byte size.
    [StructLayout(LayoutKind.Sequential)]
    public struct QueueBuffer
    {
        public uint AudioDataBytesCapacity;
        public IntPtr AudioData;
        public uint AudioDataByteSize;
    }

    // 32-bit float PCM, interleaved: what the SDK's sample source produces.
    public static StreamDescription FloatFormat(int sampleRate, int channels) => new()
    {
        SampleRate = sampleRate,
        FormatId = FormatLinearPcm,
        FormatFlags = FormatFlagIsFloat | FormatFlagIsPacked,
        BytesPerPacket = (uint)(channels * sizeof(float)),
        FramesPerPacket = 1,
        BytesPerFrame = (uint)(channels * sizeof(float)),
        ChannelsPerFrame = (uint)channels,
        BitsPerChannel = 32,
    };

    public static void Check(int status, string call)
    {
        if (status != 0)
            throw new InvalidOperationException($"{call} failed with OSStatus {status}.");
    }

    // No run loop: the callbacks come on a thread of the queue's own.
    [DllImport(Lib)]
    public static extern int AudioQueueNewOutput(in StreamDescription format, IntPtr callback, IntPtr userData,
        IntPtr runLoop, IntPtr runLoopMode, uint flags, out IntPtr queue);

    [DllImport(Lib)]
    public static extern int AudioQueueAllocateBuffer(IntPtr queue, uint byteSize, out IntPtr buffer);

    [DllImport(Lib)]
    public static extern int AudioQueueEnqueueBuffer(IntPtr queue, IntPtr buffer, uint packetDescriptions, IntPtr descriptions);

    [DllImport(Lib)]
    public static extern int AudioQueueStart(IntPtr queue, IntPtr startTime);

    [DllImport(Lib)]
    public static extern int AudioQueuePause(IntPtr queue);

    [DllImport(Lib)]
    public static extern int AudioQueueStop(IntPtr queue, byte immediate);

    [DllImport(Lib)]
    public static extern int AudioQueueDispose(IntPtr queue, byte immediate);
}
