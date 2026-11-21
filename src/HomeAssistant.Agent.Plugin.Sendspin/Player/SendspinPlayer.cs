using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.Plugin.Sendspin.Audio;
using Microsoft.Extensions.Logging;
using Sendspin.SDK.Audio;
using Sendspin.SDK.Client;
using Sendspin.SDK.Connection.Noise;
using Sendspin.SDK.Connection.Noise.Pairing;
using Sendspin.SDK.Discovery;
using Sendspin.SDK.Models;
using Sendspin.SDK.Synchronization;

namespace HomeAssistant.Agent.Plugin.Sendspin.Player;

// This computer as a Sendspin player. It announces itself on the network
// (_sendspin._tcp over mDNS) and Music Assistant connects to it — nothing to
// enter on either side but the pairing code, once per server.
internal sealed class SendspinPlayer : IAsyncDisposable
{
    private readonly ILogger<SendspinPlayer> _logger;
    private readonly IPluginNotifier _notifier;
    private readonly SendspinStore _store = new();
    private readonly PairingWindow _pairingWindow = new();
    private readonly SendspinHostService _host;
    private readonly HashSet<string> _servers = [];

    public SendspinPlayer(ILoggerFactory pluginLoggerFactory, IPluginNotifier notifier, string playerName, string softwareVersion)
    {
        _logger = pluginLoggerFactory.CreateLogger<SendspinPlayer>();
        _notifier = notifier;

        var loggerFactory = new SdkLoggerFactory(pluginLoggerFactory);
        var store = _store;
        var clockSync = new KalmanClockSynchronizer();

        var pipeline = new AudioPipeline(
            loggerFactory.CreateLogger<AudioPipeline>(),
            new AudioDecoderFactory(loggerFactory),
            clockSync,
            bufferFactory: (format, sync) => new TimedAudioBuffer(format, sync),
            playerFactory: () => CreateOutput(pluginLoggerFactory),
            sourceFactory: (buffer, now) => new SyncCorrectedSampleSource(buffer, now));

        _host = new SendspinHostService(
            loggerFactory,
            new SendspinClientOptions
            {
                Identity = SendspinIdentity.FromStore(new FileSendspinIdentityStore(store.IdentityFile)),
                PairingRecordStore = new FilePairingRecordStore(store.PairingFile),
                PairingCodeLockoutStore = new FilePairingCodeLockoutStore(store.LockoutFile),
                OutputDelayStore = store,
                PresentPairingCodeAsync = PresentPairingCodeAsync,
                PairingWindow = _pairingWindow,
                ClockSynchronizer = clockSync,
                AudioPipeline = pipeline,
                Capabilities = new ClientCapabilities
                {
                    ClientName = playerName,
                    ProductName = "Home Assistant Agent",
                    Manufacturer = "Navatusein",
                    SoftwareVersion = softwareVersion,
                    // Both lists come with defaults: assigned, not added to.
                    Roles = ["player@v1"],
                    AudioFormats =
                    [
                        new AudioFormat { Codec = AudioCodecs.Flac, SampleRate = 48000, Channels = 2 },
                        new AudioFormat { Codec = AudioCodecs.Pcm, SampleRate = 48000, Channels = 2, BitDepth = 16 },
                        new AudioFormat { Codec = AudioCodecs.Opus, SampleRate = 48000, Channels = 2, Bitrate = 256 },
                    ],
                    PairingCodeMethods = ["dynamic_pairing_code"],
                },
            },
            advertiserOptions: new AdvertiserOptions
            {
                InstanceName = $"ha-agent-{Environment.MachineName.ToLowerInvariant()}",
                PlayerName = playerName,
            },
            lastPlayedServerStore: store);

        _host.ServerConnected += Host_OnServerConnected;
        _host.ServerDisconnected += Host_OnServerDisconnected;
        _host.PairingCompleted += Host_OnPairingCompleted;
        _host.PairingGestureRequested += Host_OnPairingGestureRequested;
    }

    // The audio output of this system: the plugin runs on these two only.
    private static IAudioPlayer CreateOutput(ILoggerFactory loggerFactory)
    {
        if (OperatingSystem.IsWindows())
            return new WasapiAudioPlayer(loggerFactory.CreateLogger<WasapiAudioPlayer>());

        if (OperatingSystem.IsMacOS())
            return new AudioQueuePlayer(loggerFactory.CreateLogger<AudioQueuePlayer>());

        throw new PlatformNotSupportedException("The Sendspin player plays on Windows and macOS only.");
    }

    // Whether a Music Assistant server is connected to the player.
    public bool IsConnected
    {
        get
        {
            lock (_servers)
                return _servers.Count > 0;
        }
    }

    public event Action? ConnectionChanged;

    public Task StartAsync()
    {
        // Switching the plugin off and on is the deliberate gesture a locked
        // pairing waits for (see Host_OnPairingGestureRequested): the pairing
        // window opens for five minutes. Only then — otherwise every start
        // would lift the lockout.
        if (_store.TakePairingGesture())
        {
            _pairingWindow.Open();
            _logger.LogInformation("Pairing allowed for {Minutes} minutes", PairingWindow.DefaultLifetime.TotalMinutes);
        }

        return _host.StartAsync();
    }

    // Music Assistant asks for the code once the user starts pairing there.
    private ValueTask PresentPairingCodeAsync(PairingCodePresentation presentation, CancellationToken cancellationToken)
    {
        var code = string.Join(" ", presentation.Groups);

        _logger.LogInformation("Pairing code for Music Assistant: {Code}", code);
        _notifier.Show(new PluginNotification("Music Assistant pairing", $"Enter {code} in Music Assistant.")
        {
            Important = true
        });

        return ValueTask.CompletedTask;
    }

    private void Host_OnServerConnected(object? sender, ConnectedServerInfo server)
    {
        _logger.LogInformation("Connected to {Server}", server.ServerName);
        SetConnected(server.ServerId, true);
    }

    private void Host_OnServerDisconnected(object? sender, string serverId)
    {
        _logger.LogInformation("Disconnected from {ServerId}", serverId);
        SetConnected(serverId, false);
    }

    private void Host_OnPairingCompleted(object? sender, string serverId) =>
        _logger.LogInformation("Paired with {ServerId}", serverId);

    // Repeated wrong codes make the SDK hold further attempts until someone at
    // this computer allows them: the next start of the plugin does.
    private void Host_OnPairingGestureRequested(object? sender, PairingGestureRequestedEventArgs e)
    {
        if (_pairingWindow.IsOpen)
            return;

        _logger.LogWarning("Pairing is locked after wrong codes");
        _store.RequestPairingGesture();
        _notifier.Show(new PluginNotification("Music Assistant pairing locked",
            "Too many wrong codes. Switch the Sendspin plugin off and on to allow pairing for 5 minutes.")
        {
            Important = true
        });
    }

    private void SetConnected(string serverId, bool connected)
    {
        bool changed;

        lock (_servers)
            changed = connected ? _servers.Add(serverId) : _servers.Remove(serverId);

        if (changed)
            ConnectionChanged?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        _host.ServerConnected -= Host_OnServerConnected;
        _host.ServerDisconnected -= Host_OnServerDisconnected;
        _host.PairingCompleted -= Host_OnPairingCompleted;
        _host.PairingGestureRequested -= Host_OnPairingGestureRequested;

        await _host.StopAsync();
        await _host.DisposeAsync();
    }
}
