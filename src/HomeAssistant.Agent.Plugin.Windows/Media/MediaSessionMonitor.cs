﻿using System.Collections.Concurrent;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel;
using Windows.Graphics.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace HomeAssistant.Agent.Plugin.Windows.Media;

// What the current media session reports. Album and App are Unknown when the
// player gives none; Cover is a PNG, or null when there is no picture.
public sealed record NowPlayingInfo(string Text, string Album, string App, byte[]? Cover);

// Watches the Windows global media session (the same source the system
// media overlay uses) and reports what is currently playing. Works for any
// player that integrates with the system media transport controls (Spotify,
// browsers, the Media Player app, ...).
public sealed class MediaSessionMonitor : IDisposable
{
    public const string NothingPlaying = "Nothing playing";

    // Home Assistant reads this sensor payload as "unknown"; an empty one it
    // ignores and keeps the previous value.
    public const string Unknown = "None";

    // Covers are scaled down to fit this square: players hand out anything
    // from 300 px album art to full-size video frames.
    private const uint MaxCoverSize = 512;

    // AUMID -> display name; resolving one goes through the package catalog.
    private static readonly ConcurrentDictionary<string, string> AppNames = new();

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;

    // Raised when the current session or its track changes; the handler
    // should pull the new info via GetNowPlayingAsync. Fires on WinRT
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

    public async Task<NowPlayingInfo> GetNowPlayingAsync()
    {
        var session = _session;

        if (session == null)
            return new NowPlayingInfo(NothingPlaying, Unknown, Unknown, null);

        try
        {
            var properties = await session.TryGetMediaPropertiesAsync();

            var title = properties.Title?.Trim();
            var artist = properties.Artist?.Trim();
            var album = properties.AlbumTitle?.Trim();
            var app = GetAppName(session.SourceAppUserModelId);

            if (string.IsNullOrEmpty(title))
                return new NowPlayingInfo(NothingPlaying, Unknown, app, null);

            var text = string.IsNullOrEmpty(artist) ? title : $"{artist} - {title}";
            var cover = await ReadCoverAsync(properties.Thumbnail);

            return new NowPlayingInfo(text, string.IsNullOrEmpty(album) ? Unknown : album, app, cover);
        }
        catch
        {
            // The session can die between the null check and the query
            // (player closed) — treat it as nothing playing.
            return new NowPlayingInfo(NothingPlaying, Unknown, Unknown, null);
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

    // A 1x1 transparent PNG: the cover while there is none.
    public static async Task<byte[]> CreateEmptyCoverAsync()
    {
        using var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, 1, 1, BitmapAlphaMode.Premultiplied);
        return await EncodePngAsync(bitmap);
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

    // The thumbnail in whatever format the player gave, re-encoded as a PNG
    // no bigger than MaxCoverSize. null when there is none or it cannot be
    // read — the track info still goes out.
    private static async Task<byte[]?> ReadCoverAsync(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail == null)
            return null;

        try
        {
            using var stream = await thumbnail.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);

            var width = decoder.OrientedPixelWidth;
            var height = decoder.OrientedPixelHeight;
            var scale = Math.Min(1.0, (double) MaxCoverSize / Math.Max(width, height));

            var transform = new BitmapTransform
            {
                ScaledWidth = (uint) Math.Max(1, Math.Round(width * scale)),
                ScaledHeight = (uint) Math.Max(1, Math.Round(height * scale)),
                InterpolationMode = BitmapInterpolationMode.Fant
            };

            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied, transform, ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb);

            return await EncodePngAsync(bitmap);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<byte[]> EncodePngAsync(SoftwareBitmap bitmap)
    {
        using var output = new InMemoryRandomAccessStream();

        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();

        var bytes = new byte[output.Size];
        output.Seek(0);
        await output.ReadAsync(bytes.AsBuffer(), (uint) bytes.Length, InputStreamOptions.None);

        return bytes;
    }

    // Packaged players (Media Player, Spotify from the Store) resolve to
    // their display name; a desktop one reports its exe name, shown without
    // ".exe". Anything else shows the raw id.
    private static string GetAppName(string? appUserModelId)
    {
        if (string.IsNullOrEmpty(appUserModelId))
            return Unknown;

        return AppNames.GetOrAdd(appUserModelId, id =>
        {
            try
            {
                var name = AppInfo.GetFromAppUserModelId(id).DisplayInfo.DisplayName;

                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
            catch
            {
                // Not a packaged app.
            }

            return id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? id[..^4] : id;
        });
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
