using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Sendspin.SDK.Audio;
using Sendspin.SDK.Models;

namespace HomeAssistant.Agent.Plugin.Sendspin.Audio;

// Plays the SDK's float samples on the default output device through WASAPI,
// shared mode, event driven. The stream's own format goes to the device and
// Windows converts it to the mix format (AUTOCONVERTPCM), so any sample rate
// Music Assistant sends plays without a resampler of ours.
//
// Everything Core Audio runs on one thread of the player's own, created for
// the stream: the COM objects never cross threads, and the render loop is
// what the device waits on. The stream follows the default device: when the
// user picks another one, or the current one goes away (unplugged headphones,
// a sleep), it opens again on whatever is the default now.
[SupportedOSPlatform("windows")]
internal sealed class WasapiAudioPlayer(ILogger<WasapiAudioPlayer> logger) : IAudioPlayer, IMMNotificationClient
{
    // The device buffer: what keeps the output fed between two wake-ups. It is
    // also most of the latency, which the SDK compensates for.
    private const int BufferMs = 40;

    // How often a stream without a device tries again.
    private static readonly TimeSpan ReopenDelay = TimeSpan.FromSeconds(1);

    private Thread? _thread;
    private AutoResetEvent? _wake;
    private volatile bool _closing;
    private volatile bool _running;
    private volatile bool _reopen;
    private IAudioSampleSource? _source;

    private int _sampleRate;
    private int _channels;

    // Read on the render thread through GetCurrentOutputLatencyMicroseconds,
    // while it is inside the sample source's Read.
    private long _queuedFrames;
    private long _streamLatencyMicroseconds;
    private int _bufferMs = BufferMs;

    public AudioPlayerState State { get; private set; } = AudioPlayerState.Uninitialized;

    // Amplitude, set by the SDK from the volume Music Assistant sends.
    public float Volume { get; set; } = 1f;

    public bool IsMuted { get; set; }

    public int OutputLatencyMs => _bufferMs;

    public event EventHandler<AudioPlayerState>? StateChanged;

    public event EventHandler<AudioPlayerError>? ErrorOccurred;

    public Task InitializeAsync(AudioFormat format, CancellationToken cancellationToken = default)
    {
        CloseStream();

        _sampleRate = format.SampleRate;
        _channels = format.Channels;
        _closing = false;
        _running = false;
        _reopen = false;
        _wake = new AutoResetEvent(false);

        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _thread = new Thread(() => Render(opened))
        {
            Name = "Sendspin audio",
            IsBackground = true,
            Priority = ThreadPriority.Highest,
        };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();

        return opened.Task.WaitAsync(cancellationToken);
    }

    public void SetSampleSource(IAudioSampleSource source) => _source = source;

    public void Play()
    {
        _running = true;
        _wake?.Set();
        SetState(AudioPlayerState.Playing);
    }

    public void Pause()
    {
        _running = false;
        _wake?.Set();
        SetState(AudioPlayerState.Paused);
    }

    public void Stop()
    {
        _running = false;
        _wake?.Set();
        SetState(AudioPlayerState.Stopped);
    }

    // The player has no device choice of its own: it plays where Windows does.
    public Task SwitchDeviceAsync(string? deviceId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    // What is queued in the device ahead of the block being filled, plus the
    // device's own latency after it (see IAudioPlayer).
    public long? GetCurrentOutputLatencyMicroseconds() =>
        _sampleRate == 0 ? null : Interlocked.Read(ref _queuedFrames) * 1_000_000L / _sampleRate + _streamLatencyMicroseconds;

    private void Render(TaskCompletionSource opened)
    {
        IMMDeviceEnumerator? enumerator = null;

        try
        {
            enumerator = Wasapi.CreateDeviceEnumerator();
            enumerator.RegisterEndpointNotificationCallback(this);

            while (!_closing)
            {
                PlayOnDefaultDevice(enumerator, opened);

                if (!_closing && !_reopen)
                    _wake!.WaitOne(ReopenDelay);

                _reopen = false;
            }
        }
        catch (Exception ex) when (opened.TrySetException(ex))
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Audio output failed");
            SetState(AudioPlayerState.Error);
            ErrorOccurred?.Invoke(this, new AudioPlayerError(ex.Message, ex));
        }
        finally
        {
            try
            {
                enumerator?.UnregisterEndpointNotificationCallback(this);
            }
            catch (COMException)
            {
                // Nothing to unregister from any more.
            }
        }
    }

    // Plays until the stream has to be opened again; returns then. Completes
    // opened (InitializeAsync) once the device is open. A failure before that
    // fails InitializeAsync, so the SDK sees a player that cannot play; after
    // it, a failure only means a moment without a device.
    private void PlayOnDefaultDevice(IMMDeviceEnumerator enumerator, TaskCompletionSource opened)
    {
        var format = IntPtr.Zero;
        IAudioClient? client = null;

        try
        {
            enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var device);
            device.Activate(Wasapi.AudioClientInterface, Wasapi.ClsCtxAll, IntPtr.Zero, out var activated);
            client = (IAudioClient)activated;

            format = Wasapi.AllocFloatFormat(_sampleRate, _channels);
            client.Initialize(AudioClientShareMode.Shared,
                Wasapi.StreamFlagsEventCallback | Wasapi.StreamFlagsAutoConvertPcm | Wasapi.StreamFlagsSrcDefaultQuality,
                BufferMs * Wasapi.TicksPerMillisecond, 0, format, IntPtr.Zero);

            client.GetBufferSize(out var bufferFrames);
            client.GetStreamLatency(out var streamLatency);
            client.GetService(Wasapi.AudioRenderClientInterface, out var service);
            var renderClient = (IAudioRenderClient)service;

            using var deviceEvent = new AutoResetEvent(false);
            client.SetEventHandle(deviceEvent.SafeWaitHandle.DangerousGetHandle());

            _streamLatencyMicroseconds = streamLatency / 10;
            _bufferMs = (int)(bufferFrames * 1000L / _sampleRate);

            logger.LogInformation("Playing {SampleRate} Hz, {Channels} ch on {Device}; buffer {Buffer} ms",
                _sampleRate, _channels, Wasapi.GetFriendlyName(device), _bufferMs);

            if (State == AudioPlayerState.Uninitialized)
                SetState(AudioPlayerState.Stopped);

            opened.TrySetResult();
            RenderLoop(client, renderClient, bufferFrames, deviceEvent);
        }
        catch (COMException ex) when (opened.Task.IsCompleted)
        {
            logger.LogWarning("Audio device unavailable ({Error:X8}); retrying", ex.HResult);
        }
        finally
        {
            try
            {
                client?.Stop();
            }
            catch (COMException)
            {
                // The device is gone; nothing left to stop.
            }

            if (format != IntPtr.Zero)
                Marshal.FreeCoTaskMem(format);
        }
    }

    private void RenderLoop(IAudioClient client, IAudioRenderClient renderClient, uint bufferFrames, AutoResetEvent deviceEvent)
    {
        var samples = new float[bufferFrames * _channels];
        var started = false;
        WaitHandle[] handles = [deviceEvent, _wake!];

        while (!_closing && !_reopen)
        {
            if (!_running)
            {
                if (started)
                {
                    client.Stop();
                    started = false;
                }

                WaitHandle.WaitAny(handles, 500);
                continue;
            }

            client.GetCurrentPadding(out var padding);
            var frames = bufferFrames - padding;

            if (frames > 0)
            {
                var count = (int)(frames * _channels);

                Interlocked.Exchange(ref _queuedFrames, padding);
                Fill(samples, count);

                renderClient.GetBuffer(frames, out var data);
                Marshal.Copy(samples, 0, data, count);
                renderClient.ReleaseBuffer(frames, 0);
            }

            // Started once the buffer holds audio, so the first block is not
            // played out of an empty device.
            if (!started)
            {
                client.Start();
                started = true;
            }

            WaitHandle.WaitAny(handles, 2000);
        }
    }

    // No allocation here: this runs every few milliseconds on the render thread.
    private void Fill(float[] samples, int count)
    {
        var block = samples.AsSpan(0, count);

        if (_source == null)
        {
            block.Clear();
            return;
        }

        _source.Read(samples, 0, count);

        var gain = IsMuted ? 0f : Volume;

        if (gain != 1f)
        {
            foreach (ref var sample in block)
                sample *= gain;
        }
    }

    private void RequestReopen()
    {
        _reopen = true;
        _wake?.Set();
    }

    // Called by Windows on a thread of its own: only flags the render thread.
    void IMMNotificationClient.OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId)
    {
        if (flow == EDataFlow.Render && role == ERole.Multimedia)
            RequestReopen();
    }

    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, int newState) { }

    void IMMNotificationClient.OnDeviceAdded(string deviceId) { }

    void IMMNotificationClient.OnDeviceRemoved(string deviceId) { }

    void IMMNotificationClient.OnPropertyValueChanged(string deviceId, PropertyKey key) { }

    private void CloseStream()
    {
        _closing = true;
        _wake?.Set();
        _thread?.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        _wake?.Dispose();
        _wake = null;
    }

    private void SetState(AudioPlayerState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }

    public ValueTask DisposeAsync()
    {
        CloseStream();
        return ValueTask.CompletedTask;
    }
}
