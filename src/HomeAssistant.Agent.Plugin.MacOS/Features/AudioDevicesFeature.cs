using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.Plugin.Base.Config.Controls;
using HomeAssistant.Agent.Plugin.Base.Interfaces;
using HomeAssistant.Agent.Plugin.MacOS.Audio;
using HomeAssistant.Agent.Plugin.MacOS.Config;
using HomeAssistant.Connector.Entities;

namespace HomeAssistant.Agent.Plugin.MacOS.Features;

// Switches the default output and input device between the ones the user
// listed, and follows changes made in macOS.
internal sealed class AudioDevicesFeature : PluginFeature
{
    private List<AudioDevice> _outputDevices = [];
    private List<AudioDevice> _inputDevices = [];

    private SelectEntity? _outputDeviceSelect;
    private SelectEntity? _inputDeviceSelect;

    private AudioDeviceMonitor? _deviceMonitor;

    public AudioDevicesFeature(string outputOptionsTool, string inputOptionsTool)
        : base("audio-devices", "Audio Devices", "Switches the default output and input device.",
            PluginHostMode.Desktop)
    {
        Controls =
        [
            new ComplexListControlDescriptor("OutputDevices", "Output Devices", "One option per device.",
                new DynamicSelectControlDescriptor("DeviceName", "Device Name",
                    "Output device in macOS.", outputOptionsTool),
                new InputTextControlDescriptor("FriendlyName", "Friendly Name",
                    "Name shown in Home Assistant.")
            ),
            new ComplexListControlDescriptor("InputDevices", "Input Devices", "One option per device.",
                new DynamicSelectControlDescriptor("DeviceName", "Device Name",
                    "Input device in macOS.", inputOptionsTool),
                new InputTextControlDescriptor("FriendlyName", "Friendly Name",
                    "Name shown in Home Assistant.")
            ),
        ];
    }

    public override IEntity[] GetEntities() =>
        new[] { _outputDeviceSelect, _inputDeviceSelect }.OfType<IEntity>().ToArray();

    protected override void OnConfigured(IPluginConfig config)
    {
        var macConfig = (MacOsPluginConfig) config;

        _outputDevices = macConfig.OutputDevices;
        _inputDevices = macConfig.InputDevices;

        _outputDeviceSelect = BuildDeviceSelect(_outputDevices, AudioScope.Output, "mdi:speaker");
        _inputDeviceSelect = BuildDeviceSelect(_inputDevices, AudioScope.Input, "mdi:microphone");
    }

    protected override async Task OnEnableAsync()
    {
        _deviceMonitor = new AudioDeviceMonitor(CreateLogger<AudioDeviceMonitor>());
        _deviceMonitor.DefaultDeviceChanged += Device_OnDefaultChanged;

        await PublishCurrentDeviceAsync(_outputDeviceSelect, _outputDevices, AudioScope.Output);
        await PublishCurrentDeviceAsync(_inputDeviceSelect, _inputDevices, AudioScope.Input);
    }

    protected override Task OnDisableAsync()
    {
        if (_deviceMonitor != null)
        {
            _deviceMonitor.DefaultDeviceChanged -= Device_OnDefaultChanged;
            _deviceMonitor.Dispose();
            _deviceMonitor = null;
        }

        return Task.CompletedTask;
    }

    private void Device_OnDefaultChanged(AudioScope scope, string? deviceName)
    {
        var select = scope == AudioScope.Output ? _outputDeviceSelect : _inputDeviceSelect;
        var devices = scope == AudioScope.Output ? _outputDevices : _inputDevices;

        if (select == null || deviceName == null)
            return;

        var match = devices.FirstOrDefault(d => d.DeviceName == deviceName);

        if (match != null)
            FireAndForget(select.SetStateAsync(match.DisplayName));
    }

    private static SelectEntity? BuildDeviceSelect(List<AudioDevice> devices, AudioScope scope, string icon)
    {
        if (devices.Count == 0)
            return null;

        var kind = scope == AudioScope.Output ? "output" : "input";

        return new SelectEntity
        {
            Name = $"{(scope == AudioScope.Output ? "Output" : "Input")} Device",
            UniqueId = $"macos_audio_devices_{kind}",
            BaseTopic = $"macos/audio-devices/{kind}",
            Icon = icon,
            Options = [..devices.Select(d => d.DisplayName)],
            // No DefaultState: OnEnableAsync publishes the real device right
            // after, and a default would flash the first option first.
            ChangeStateHandler = displayName => DeviceSelect_ChangeStateHandler(scope, devices, displayName)
        };
    }

    // Throws when the switch fails: the host then logs it and keeps the
    // published state, so Home Assistant goes on showing the real device.
    private static Task<string> DeviceSelect_ChangeStateHandler(AudioScope scope, List<AudioDevice> devices,
        string displayName)
    {
        var device = devices.FirstOrDefault(d => d.DisplayName == displayName);

        if (device != null)
            AudioDevices.SetDefault(scope, device.DeviceName);

        return Task.FromResult(displayName);
    }

    private static async Task PublishCurrentDeviceAsync(SelectEntity? select, List<AudioDevice> devices,
        AudioScope scope)
    {
        if (select == null)
            return;

        var current = AudioDevices.GetDefaultName(scope);
        var match = devices.FirstOrDefault(d => d.DeviceName == current);

        if (match != null)
            await select.SetStateAsync(match.DisplayName);
    }
}
