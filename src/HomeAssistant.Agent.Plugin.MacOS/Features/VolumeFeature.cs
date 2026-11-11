using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.Plugin.MacOS.Audio;
using HomeAssistant.Connector.Entities;

namespace HomeAssistant.Agent.Plugin.MacOS.Features;

// Volume and mute of the default output device, kept in sync both ways.
internal sealed class VolumeFeature : PluginFeature
{
    private readonly NumberEntity _volumeNumber;
    private readonly SwitchEntity _muteSwitch;

    private SystemVolumeController? _volume;

    public VolumeFeature()
        : base("volume", "Volume", "Volume and mute of the default output device.", PluginHostMode.Desktop)
    {
        _volumeNumber = new NumberEntity
        {
            Name = "Volume",
            UniqueId = "macos_volume_level",
            BaseTopic = "macos/volume/level",
            Icon = "mdi:volume-high",
            Min = 0,
            Max = 100,
            Step = 1,
            UnitOfMeasurement = "%",
            PublishDefaultState = false,
            ChangeStateHandler = Volume_ChangeStateHandler
        };

        _muteSwitch = new SwitchEntity
        {
            Name = "Mute",
            UniqueId = "macos_volume_mute",
            BaseTopic = "macos/volume/mute",
            Icon = "mdi:volume-mute",
            PublishDefaultState = false,
            ChangeStateHandler = Mute_ChangeStateHandler
        };
    }

    public override IEntity[] GetEntities() => [_volumeNumber, _muteSwitch];

    protected override async Task OnEnableAsync()
    {
        _volume = new SystemVolumeController(CreateLogger<SystemVolumeController>());
        _volume.Changed += Volume_OnChanged;

        await _volumeNumber.SetStateAsync(_volume.GetVolumePercent());
        await _muteSwitch.SetStateAsync(_volume.GetMute());
    }

    protected override Task OnDisableAsync()
    {
        if (_volume != null)
        {
            _volume.Changed -= Volume_OnChanged;
            _volume.Dispose();
            _volume = null;
        }

        return Task.CompletedTask;
    }

    private void Volume_OnChanged(double percent, bool mute)
    {
        FireAndForget(_volumeNumber.SetStateAsync(percent));
        FireAndForget(_muteSwitch.SetStateAsync(mute));
    }

    private Task<double> Volume_ChangeStateHandler(double newValue)
    {
        _volume?.SetVolumePercent(newValue);
        return Task.FromResult(Math.Clamp(newValue, 0, 100));
    }

    private Task<bool> Mute_ChangeStateHandler(bool newState)
    {
        _volume?.SetMute(newState);
        return Task.FromResult(newState);
    }
}
