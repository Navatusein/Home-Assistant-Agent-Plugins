using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Sendspin.SDK.Audio;
using Sendspin.SDK.Models;

namespace HomeAssistant.Agent.Plugin.Sendspin.Audio;

// Plays the SDK's float samples on macOS through an Audio Queue on the default
// output device, which macOS follows when the user picks another one. A few
// short buffers circulate: the queue hands each back once played and the
// callback fills it from the SDK's sample source and enqueues it again.
//
// The queue keeps running while paused or stopped, playing silence: the
// buffers never leave the cycle, so Play needs nothing to prime.
[SupportedOSPlatform("macos")]
internal sealed class AudioQueuePlayer(ILogger<AudioQueuePlayer> logger) : IAudioPlayer
{
    private const int BufferCount = 3;
    private const int BufferMs = 20;

    // One callback for every player: a delegate handed to native code must
    // stay alive as long as the code may call it. userData leads back to the
    // player.
    private static readonly AudioToolbox.OutputCallback Callback = OnBufferDone;
    private static readonly IntPtr CallbackPointer = Marshal.GetFunctionPointerForDelegate(Callback);

    private readonly object _lock = new();
    private IntPtr _queue;
    private GCHandle _self;
    private IAudioSampleSource? _source;
    private volatile bool _running;

    private int _sampleRate;
    private int _channels;
    private float[] _samples = [];

    public AudioPlayerState State { get; private set; } = AudioPlayerState.Uninitialized;

    // Amplitude, set by the SDK from the volume Music Assistant sends.
    public float Volume { get; set; } = 1f;

    public bool IsMuted { get; set; }

    public int OutputLatencyMs => BufferCount * BufferMs;

    public event EventHandler<AudioPlayerState>? StateChanged;

    public event EventHandler<AudioPlayerError>? ErrorOccurred;

    public Task InitializeAsync(AudioFormat format, CancellationToken cancellationToken = default)
    {
        CloseQueue();

        _sampleRate = format.SampleRate;
        _channels = format.Channels;

        var frames = _sampleRate * BufferMs / 1000;
        var bytes = (uint)(frames * _channels * sizeof(float));
        _samples = new float[frames * _channels];

        lock (_lock)
        {
            _self = GCHandle.Alloc(this);

            var description = AudioToolbox.FloatFormat(_sampleRate, _channels);
            AudioToolbox.Check(AudioToolbox.AudioQueueNewOutput(description, CallbackPointer, GCHandle.ToIntPtr(_self),
                IntPtr.Zero, IntPtr.Zero, 0, out _queue), "AudioQueueNewOutput");

            // Primed with silence; the cycle starts with AudioQueueStart.
            for (var i = 0; i < BufferCount; i++)
            {
                AudioToolbox.Check(AudioToolbox.AudioQueueAllocateBuffer(_queue, bytes, out var buffer), "AudioQueueAllocateBuffer");
                Fill(buffer);
                AudioToolbox.Check(AudioToolbox.AudioQueueEnqueueBuffer(_queue, buffer, 0, IntPtr.Zero), "AudioQueueEnqueueBuffer");
            }
        }

        logger.LogInformation("Playing {SampleRate} Hz, {Channels} ch on the default output; buffer {Buffer} ms",
            _sampleRate, _channels, OutputLatencyMs);

        SetState(AudioPlayerState.Stopped);
        return Task.CompletedTask;
    }

    public void SetSampleSource(IAudioSampleSource source) => _source = source;

    public void Play()
    {
        _running = true;

        lock (_lock)
        {
            if (_queue != IntPtr.Zero)
                Report(AudioToolbox.AudioQueueStart(_queue, IntPtr.Zero), "AudioQueueStart");
        }

        SetState(AudioPlayerState.Playing);
    }

    public void Pause()
    {
        _running = false;
        SetState(AudioPlayerState.Paused);
    }

    public void Stop()
    {
        _running = false;
        SetState(AudioPlayerState.Stopped);
    }

    // The player has no device choice of its own: it plays where macOS does.
    public Task SwitchDeviceAsync(string? deviceId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    // The buffers ahead of the one being filled.
    public long? GetCurrentOutputLatencyMicroseconds() => (BufferCount - 1) * BufferMs * 1000L;

    private static void OnBufferDone(IntPtr userData, IntPtr queue, IntPtr buffer)
    {
        if (GCHandle.FromIntPtr(userData).Target is not AudioQueuePlayer player)
            return;

        player.Fill(buffer);
        AudioToolbox.AudioQueueEnqueueBuffer(queue, buffer, 0, IntPtr.Zero);
    }

    // On the queue's thread: no allocation here.
    private void Fill(IntPtr buffer)
    {
        var block = _samples.AsSpan();

        if (_running && _source != null)
        {
            _source.Read(_samples, 0, _samples.Length);

            var gain = IsMuted ? 0f : Volume;

            if (gain != 1f)
            {
                foreach (ref var sample in block)
                    sample *= gain;
            }
        }
        else
        {
            block.Clear();
        }

        var head = Marshal.PtrToStructure<AudioToolbox.QueueBuffer>(buffer);
        var bytes = Math.Min((uint)(_samples.Length * sizeof(float)), head.AudioDataBytesCapacity);

        Marshal.Copy(_samples, 0, head.AudioData, (int)(bytes / sizeof(float)));
        Marshal.WriteInt32(buffer, Marshal.OffsetOf<AudioToolbox.QueueBuffer>(nameof(AudioToolbox.QueueBuffer.AudioDataByteSize)).ToInt32(), (int)bytes);
    }

    private void Report(int status, string call)
    {
        if (status == 0)
            return;

        logger.LogError("{Call} failed with OSStatus {Status}", call, status);
        SetState(AudioPlayerState.Error);
        ErrorOccurred?.Invoke(this, new AudioPlayerError($"{call} failed with OSStatus {status}."));
    }

    private void CloseQueue()
    {
        lock (_lock)
        {
            if (_queue != IntPtr.Zero)
            {
                // Immediate: no callback runs after these return.
                AudioToolbox.AudioQueueStop(_queue, 1);
                AudioToolbox.AudioQueueDispose(_queue, 1);
                _queue = IntPtr.Zero;
            }

            if (_self.IsAllocated)
                _self.Free();
        }
    }

    private void SetState(AudioPlayerState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }

    public ValueTask DisposeAsync()
    {
        _running = false;
        CloseQueue();
        return ValueTask.CompletedTask;
    }
}
