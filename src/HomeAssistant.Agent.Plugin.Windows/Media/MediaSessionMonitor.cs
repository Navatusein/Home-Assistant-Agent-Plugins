using Windows.Media.Control;

namespace HomeAssistant.Agent.Plugin.Windows.Media;

// Watches the Windows global media session (the same source the system
// media overlay uses) and reports what is currently playing. Works for any
// player that integrates with the system media transport controls (Spotify,
// browsers, the Media Player app, ...).
public sealed class MediaSessionMonitor : IDisposable
{
    public const string NothingPlaying = "Nothing playing";

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;

    // Raised when the current session or its track changes; the handler
    // should pull the new text via GetNowPlayingAsync. Fires on WinRT
    // worker threads.
    public event Action? NowPlayingChanged;

    // Raised when playback starts/pauses/stops (or the current session
    // changes); the handler should pull the new value via IsPlaying. Fires
    // on WinRT worker threads.
    public event Action? PlaybackStatusChanged;

    public async Task StartAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.CurrentSessionChanged += Manager_OnCurrentSessionChanged;

        AttachSession(_manager.GetCurrentSession());
    }

    public async Task<string> GetNowPlayingAsync()
    {
        var session = _session;

        if (session == null)
            return NothingPlaying;

        try
        {
            var properties = await session.TryGetMediaPropertiesAsync();

            var title = properties.Title?.Trim();
            var artist = properties.Artist?.Trim();

            if (string.IsNullOrEmpty(title))
                return NothingPlaying;

            return string.IsNullOrEmpty(artist) ? title : $"{artist} - {title}";
        }
        catch
        {
            // The session can die between the null check and the query
            // (player closed) — treat it as nothing playing.
            return NothingPlaying;
        }
    }

    // True only when a session is actively playing. Paused, stopped and
    // "no session" all read as false.
    public bool IsPlaying()
    {
        var session = _session;

        if (session == null)
            return false;

        try
        {
            return session.GetPlaybackInfo().PlaybackStatus
                == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch
        {
            // The session can die between the null check and the query.
            return false;
        }
    }

    public void Dispose()
    {
        if (_manager != null)
        {
            _manager.CurrentSessionChanged -= Manager_OnCurrentSessionChanged;
            _manager = null;
        }

        AttachSession(null);
    }

    private void Manager_OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args)
    {
        AttachSession(sender.GetCurrentSession());
        NowPlayingChanged?.Invoke();
        PlaybackStatusChanged?.Invoke();
    }

    private void Session_OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args)
    {
        NowPlayingChanged?.Invoke();
    }

    private void Session_OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args)
    {
        PlaybackStatusChanged?.Invoke();
    }

    private void AttachSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= Session_OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= Session_OnPlaybackInfoChanged;
        }

        _session = session;

        if (_session != null)
        {
            _session.MediaPropertiesChanged += Session_OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged += Session_OnPlaybackInfoChanged;
        }
    }
}
