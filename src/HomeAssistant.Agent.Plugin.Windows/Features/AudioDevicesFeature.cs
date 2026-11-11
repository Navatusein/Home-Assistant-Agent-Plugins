using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.Plugin.Base.Config.Controls;
using HomeAssistant.Agent.Plugin.Base.Interfaces;
using HomeAssistant.Agent.Plugin.Windows.Audio;
using HomeAssistant.Agent.Plugin.Windows.Config;
using HomeAssistant.Connector.Entities;
using NAudio.CoreAudioApi;

namespace HomeAssistant.Agent.Plugin.Windows.Features;

// Switches the default playback and recording device between the ones the
// user listed, and follows changes made in Windows.
internal sealed class AudioDevicesFeature : PluginFeature
{
    private readonly AudioDeviceSwitcher _deviceSwitcher = new();

    private List<AudioDevice> _outputDevices = [];
    private List<AudioDevice> _inputDevices = [];

    private SelectEntity? _outputDeviceSelect;
    private SelectEntity? _inputDeviceSelect;

    private AudioDeviceMonitor? _deviceMonitor;

    public AudioDevicesFeature(string outputOptionsTool, string inputOptionsTool)
        : base("audio-devices", "Audio Devices", "Switches the default playback and recording device.",
            PluginHostMode.Desktop)
    {
        Controls =
        [
            new ComplexListControlDescriptor("OutputDevices", "Output Devices", "One option per device.",
                new DynamicSelectControlDescriptor("DeviceName", "Device Name",
                    "Playback device in Windows.", outputOptionsTool),
                new DynamicSelectControlDescriptor("CommunicationDeviceName", "Communication Device Name",
                    "Playback device used for calls (optional).", outputOptionsTool),
                new InputTextControlDescriptor("FriendlyName", "Friendly Name",
                    "Name shown in Home Assistant.")
            ),
            new ComplexListControlDescriptor("InputDevices", "Input Devices", "One option per device.",
                new DynamicSelectControlDescriptor("DeviceName", "Device Name",
                    "Recording device in Windows.", inputOptionsTool),
                new InputTextControlDescriptor("FriendlyName", "Friendly Name",
                    "Name shown in Home Assistant.")
            ),
        ];
    }

    public override IEntity[] GetEntities() =>
        new[] { _outputDeviceSelect, _inputDeviceSelect }.OfType<IEntity>().ToArray();

    protected override void OnConfigured(IPluginConfig config)
    {
        var windowsConfig = (WindowsPluginConfig) config;

        _outputDevices = windowsConfig.OutputDevices;
        _inputDevices = windowsConfig.InputDevices;

        _outputDeviceSelect = BuildDeviceSelect(_outputDevices, DataFlow.Render, "output", "mdi:speaker");
        _inputDeviceSelect = BuildDeviceSelect(_inputDevices, DataFlow.Capture, "input", "mdi:microphone");
    }

    protected override async Task OnEnableAsync()
    {
        _deviceMonitor = new AudioDeviceMonitor();
        _deviceMonitor.DefaultDeviceChanged += Device_OnDefaultChanged;

        await PublishCurrentDeviceAsync(_outputDeviceSelect, _outputDevices, DataFlow.Render);
        await PublishCurrentDeviceAsync(_inputDeviceSelect, _inputDevices, DataFlow.Capture);
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

    private void Device_OnDefaultChanged(DataFlow flow, string? windowsDeviceName)
    {
        var select = flow == DataFlow.Render ? _outputDeviceSelect : _inputDeviceSelect;
        var devices = flow == DataFlow.Render ? _outputDevices : _inputDevices;

        if (select == null || windowsDeviceName == null)
            return;

        var match = devices.FirstOrDefault(d => d.DeviceName == windowsDeviceName);

        if (match != null)
            FireAndForget(select.SetStateAsync(match.DisplayName));
    }

    private SelectEntity? BuildDeviceSelect(List<AudioDevice> devices, DataFlow flow, string kind, string icon)
    {
        if (devices.Count == 0)
            return null;

        return new SelectEntity
        {
            Name = $"Windows {(flow == DataFlow.Render ? "Output" : "Input")} Device",
            UniqueId = $"windows_audio_devices_{kind}",
            BaseTopic = $"windows/audio-devices/{kind}",
            Icon = icon,
            Options = [..devices.Select(d => d.DisplayName)],
            // No DefaultState: OnEnableAsync publishes the real device right
            // away, a default would show up for a moment before it.
            ChangeStateHandler = displayName => DeviceSelect_ChangeStateHandler(flow, devices, displayName)
        };
    }

    private Task<string> DeviceSelect_ChangeStateHandler(DataFlow flow, List<AudioDevice> devices, string displayName)
    {
        var device = devices.FirstOrDefault(d => d.DisplayName == displayName);

        if (device != null)
        {
            if (flow == DataFlow.Render)
                _deviceSwitcher.TrySetDefaultPlaybackDevice(device.DeviceName, device.CommunicationDeviceName);
            else
                _deviceSwitcher.TrySetDefaultDevice(flow, device.DeviceName);
        }

        return Task.FromResult(displayName);
    }

    private async Task PublishCurrentDeviceAsync(SelectEntity? select, List<AudioDevice> devices, DataFlow flow)
    {
        if (select == null)
            return;

        var current = _deviceSwitcher.GetDefaultDeviceFriendlyName(flow);
        var match = devices.FirstOrDefault(d => d.DeviceName == current);

        if (match != null)
            await select.SetStateAsync(match.DisplayName);
    }
}
