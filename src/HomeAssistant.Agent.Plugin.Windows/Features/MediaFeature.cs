using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.Plugin.Windows.Keyboard;
using HomeAssistant.Agent.Plugin.Windows.Media;
using HomeAssistant.Connector.Entities;

namespace HomeAssistant.Agent.Plugin.Windows.Features;

// Media keys as buttons, plus what is playing right now from the Windows media
// session.
internal sealed class MediaFeature : PluginFeature
{
    private readonly ButtonEntity _playPauseButton = new()
    {
        Name = "Media Play/Pause",
        UniqueId = "windows_media_play_pause",
        BaseTopic = "windows/media/play-pause",
        Icon = "mdi:play-pause",
        ClickHandler = () =>
        {
            HotkeySender.Send(["mediaplaypause"]);
            return Task.CompletedTask;
        }
    };

    private readonly ButtonEntity _nextButton = new()
    {
        Name = "Media Next Track",
        UniqueId = "windows_media_next",
        BaseTopic = "windows/media/next",
        Icon = "mdi:skip-next",
        ClickHandler = () =>
        {
            HotkeySender.Send(["medianext"]);
            return Task.CompletedTask;
        }
    };

    private readonly ButtonEntity _previousButton = new()
    {
        Name = "Media Previous Track",
        UniqueId = "windows_media_previous",
        BaseTopic = "windows/media/previous",
        Icon = "mdi:skip-previous",
        ClickHandler = () =>
        {
            HotkeySender.Send(["mediaprev"]);
            return Task.CompletedTask;
        }
    };

    private readonly SensorEntity _nowPlayingSensor = new()
    {
        Name = "Now Playing",
        UniqueId = "windows_media_now_playing",
        BaseTopic = "windows/media/now-playing",
        Icon = "mdi:music-note",
        DefaultState = MediaSessionMonitor.NothingPlaying
    };

    private readonly SensorEntity _albumSensor = new()
    {
        Name = "Media Album",
        UniqueId = "windows_media_album",
        BaseTopic = "windows/media/album",
        Icon = "mdi:album",
        DefaultState = MediaSessionMonitor.Unknown
    };

    // The player the current session belongs to: Spotify, a browser, ...
    private readonly SensorEntity _appSensor = new()
    {
        Name = "Media App",
        UniqueId = "windows_media_app",
        BaseTopic = "windows/media/app",
        Icon = "mdi:application-outline",
        DefaultState = MediaSessionMonitor.Unknown
    };

    private readonly ImageEntity _coverImage = new()
    {
        Name = "Media Cover",
        UniqueId = "windows_media_cover",
        BaseTopic = "windows/media/cover",
        Icon = "mdi:image-album",
        ContentType = "image/png"
    };

    // ON while a session is actively playing, OFF when paused/stopped or
    // nothing is playing. No media-specific device class exists in HA, so
    // rely on the play/pause icon.
    private readonly BinarySensorEntity _playingSensor = new()
    {
        Name = "Media Playing",
        UniqueId = "windows_media_playing",
        BaseTopic = "windows/media/playing",
        Icon = "mdi:play-pause",
        DefaultState = false
    };

    // Track changes come in bursts (title first, the thumbnail a moment
    // later) on WinRT threads; one publish at a time keeps an older cover
    // from landing after a newer one.
    private readonly SemaphoreSlim _nowPlayingGate = new(1, 1);

    private MediaSessionMonitor? _monitor;
    private byte[]? _emptyCover;
    private byte[]? _publishedCover;

    public MediaFeature()
        : base("media", "Media", "Play/pause, next and previous, and what is playing with its cover.",
            PluginHostMode.Desktop)
    {
    }

    public override IEntity[] GetEntities() =>
    [
        _playPauseButton, _nextButton, _previousButton, _nowPlayingSensor, _albumSensor, _appSensor,
        _coverImage, _playingSensor
    ];

    protected override async Task OnEnableAsync()
    {
        _emptyCover ??= await MediaSessionMonitor.CreateEmptyCoverAsync();
        _publishedCover = null;

        _monitor = new MediaSessionMonitor();
        _monitor.NowPlayingChanged += Monitor_OnNowPlayingChanged;
        _monitor.PlaybackStatusChanged += Monitor_OnPlaybackStatusChanged;
        await _monitor.StartAsync();

        await PublishNowPlayingAsync();
        await PublishPlaybackStatusAsync();
    }

    protected override Task OnDisableAsync()
    {
        if (_monitor != null)
        {
            _monitor.NowPlayingChanged -= Monitor_OnNowPlayingChanged;
            _monitor.PlaybackStatusChanged -= Monitor_OnPlaybackStatusChanged;
            _monitor.Dispose();
            _monitor = null;
        }

        return Task.CompletedTask;
    }

    private void Monitor_OnNowPlayingChanged() => FireAndForget(PublishNowPlayingAsync());

    private void Monitor_OnPlaybackStatusChanged() => FireAndForget(PublishPlaybackStatusAsync());

    private async Task PublishNowPlayingAsync()
    {
        await _nowPlayingGate.WaitAsync();

        try
        {
            var monitor = _monitor;

            if (monitor == null)
                return;

            var info = await monitor.GetNowPlayingAsync();

            await _nowPlayingSensor.SetStateAsync(info.Text);
            await _albumSensor.SetStateAsync(info.Album);
            await _appSensor.SetStateAsync(info.App);

            // Properties change several times per track: the same picture
            // is not sent again.
            var cover = info.Cover ?? _emptyCover!;

            if (_publishedCover == null || !cover.AsSpan().SequenceEqual(_publishedCover))
            {
                await _coverImage.SetImageAsync(cover);
                _publishedCover = cover;
            }
        }
        finally
        {
            _nowPlayingGate.Release();
        }
    }

    private async Task PublishPlaybackStatusAsync()
    {
        if (_monitor == null)
            return;

        await _playingSensor.SetStateAsync(_monitor.IsPlaying());
    }
}
