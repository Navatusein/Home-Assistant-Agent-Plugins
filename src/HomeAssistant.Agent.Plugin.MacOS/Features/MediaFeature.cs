using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.Plugin.MacOS.Keyboard;
using HomeAssistant.Connector.Entities;

namespace HomeAssistant.Agent.Plugin.MacOS.Features;

// Media keys as buttons.
internal sealed class MediaFeature : PluginFeature
{
    private readonly ButtonEntity _playPauseButton = new()
    {
        Name = "Media Play/Pause",
        UniqueId = "macos_media_play_pause",
        BaseTopic = "macos/media/play-pause",
        Icon = "mdi:play-pause",
        ClickHandler = () =>
        {
            MediaKeys.Press(MediaKeys.PlayPause);
            return Task.CompletedTask;
        }
    };

    private readonly ButtonEntity _nextButton = new()
    {
        Name = "Media Next Track",
        UniqueId = "macos_media_next",
        BaseTopic = "macos/media/next",
        Icon = "mdi:skip-next",
        ClickHandler = () =>
        {
            MediaKeys.Press(MediaKeys.Next);
            return Task.CompletedTask;
        }
    };

    private readonly ButtonEntity _previousButton = new()
    {
        Name = "Media Previous Track",
        UniqueId = "macos_media_previous",
        BaseTopic = "macos/media/previous",
        Icon = "mdi:skip-previous",
        ClickHandler = () =>
        {
            MediaKeys.Press(MediaKeys.Previous);
            return Task.CompletedTask;
        }
    };

    public MediaFeature()
        : base("media", "Media", "Play/pause, next and previous track.", PluginHostMode.Desktop)
    {
    }

    public override IEntity[] GetEntities() => [_playPauseButton, _nextButton, _previousButton];
}
