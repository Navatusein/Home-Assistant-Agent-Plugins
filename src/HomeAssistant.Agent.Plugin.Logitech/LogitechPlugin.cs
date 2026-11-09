using System.Globalization;
using System.Text;
using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.Plugin.Base.Config;
using HomeAssistant.Agent.Plugin.Base.Config.Controls;
using HomeAssistant.Agent.Plugin.Base.Exceptions;
using HomeAssistant.Agent.Plugin.Base.Interfaces;
using HomeAssistant.Agent.Plugin.Base.Tools;
using HomeAssistant.Agent.Plugin.Logitech.Config;
using HomeAssistant.Agent.Plugin.Logitech.GHub;
using HomeAssistant.Connector.Entities;

namespace HomeAssistant.Agent.Plugin.Logitech;

// Exposes the battery level and charging state of Logitech devices as Home
// Assistant sensors, read from the local G HUB agent.
public class LogitechPlugin : BasePlugin
{
    public override string PluginCode => "logitech-plugin";

    private const string DeviceOptionsTool = "logitech-device-options";

    public override ConfigDescriptor ConfigDescriptor { get; } = new(
        new GroupControlDescriptor("Devices", "Requires Logitech G HUB to be installed and running.",
            new ComplexListControlDescriptor("Devices", "Devices", "Battery level and charging state per device.",
                new DynamicSelectControlDescriptor("Device", "Device",
                    "Device reported by G HUB.", DeviceOptionsTool),
                new InputTextControlDescriptor("Name", "Name", "Name shown in Home Assistant.")
            )
        )
    );

    public LogitechPlugin()
    {
        // Serves what the running client last saw. G HUB only reports its
        // devices over the same connection the plugin already holds, so there
        // is nothing to query on demand — if the list is empty, G HUB is not
        // connected yet and the refresh button retries.
        RegisterTool(new PluginTool
        {
            Name = DeviceOptionsTool,
            Description = "Lists the Logitech devices G HUB reported, as select options.",
            Handler = _ => Task.FromResult(PluginToolOptions.Serialize(
                _deviceNames.Values.Order(StringComparer.CurrentCultureIgnoreCase)))
        });

        // Rebuilt from the configured devices in OnConfigured, hence read
        // through a function.
        AddFeature(new EntityFeature("battery", "Battery",
            "Battery level and charging state per device.", PluginHostMode.Desktop,
            () => [.. _sensors.Values.Cast<IEntity>(), .. _chargingSensors.Values.Cast<IEntity>()]));
    }

    // Always a new config — the running plugin still reads the current one. A
    // list the form did not send (or sent broken) keeps the current entries.
    public override IPluginConfig CreateConfig(ConfigValues values)
    {
        var devices = values.GetList<LogitechDevice>("Devices");

        devices?.RemoveAll(device =>
            string.IsNullOrWhiteSpace(device.Device) || string.IsNullOrWhiteSpace(device.Name));

        return new LogitechPluginConfig
        {
            Devices = devices ?? (Config as LogitechPluginConfig)?.Devices ?? []
        };
    }

    private GHubClient? _client;

    // Configured device (id or display name) -> its sensors. Both dictionaries
    // are keyed the same way, so a battery update updates the pair at once.
    private readonly Dictionary<string, SensorEntity> _sensors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BinarySensorEntity> _chargingSensors = new(StringComparer.OrdinalIgnoreCase);

    // G HUB device id -> display name, so a sensor configured by name can be
    // matched against battery updates, which only carry the id.
    private readonly Dictionary<string, string> _deviceNames = new(StringComparer.OrdinalIgnoreCase);

    protected override void OnConfigured()
    {
        if (Config is not LogitechPluginConfig config)
            throw new PluginNotInitializedException("LogitechPluginConfig cannot be null!");

        _sensors.Clear();
        _chargingSensors.Clear();

        foreach (var device in config.Devices)
        {
            var slug = Slug.ToObjectId(device.Name, fallback: "device");
            var key = device.Device.Trim();

            _sensors[key] = new SensorEntity
            {
                Name = $"{device.Name} Battery",
                UniqueId = $"logitech_battery_{slug}_level",
                BaseTopic = $"logitech/battery/{slug}-level",
                Icon = "mdi:battery",
                DeviceClass = "battery",
                UnitOfMeasurement = "%",
                DefaultState = "0"
            };

            _chargingSensors[key] = new BinarySensorEntity
            {
                Name = $"{device.Name} Charging",
                UniqueId = $"logitech_battery_{slug}_charging",
                BaseTopic = $"logitech/battery/{slug}-charging",
                Icon = "mdi:battery-charging",
                DeviceClass = "battery_charging",
                DefaultState = false
            };
        }
    }

    protected override Task OnEnableAsync()
    {
        if (Config is not LogitechPluginConfig)
            throw new PluginNotInitializedException("LogitechPluginConfig cannot be null!");

        _client = new GHubClient(CreateLogger<GHubClient>());

        _client.ConnectionChanged += Client_OnConnectionChanged;
        _client.DevicesDiscovered += Client_OnDevicesDiscovered;
        _client.BatteryUpdated += Client_OnBatteryUpdated;
        _client.Start();

        return Task.CompletedTask;
    }

    protected override Task OnDisableAsync()
    {
        if (_client != null)
        {
            _client.ConnectionChanged -= Client_OnConnectionChanged;
            _client.DevicesDiscovered -= Client_OnDevicesDiscovered;
            _client.BatteryUpdated -= Client_OnBatteryUpdated;
            _client.Dispose();
            _client = null;
        }

        _deviceNames.Clear();

        return Task.CompletedTask;
    }

    // A battery level is only meaningful once G HUB has actually reported one:
    // until then the sensor would publish a made-up value, so it stays
    // unavailable instead.
    protected override Task PublishInitialAvailabilityAsync() => SetEntitiesAvailabilityAsync(false);

    private void Client_OnConnectionChanged()
    {
        // Losing G HUB says nothing about the devices' real battery level —
        // report unknown rather than a stale reading.
        if (_client is not { IsConnected: true })
            FireAndForget(SetEntitiesAvailabilityAsync(false));
    }

    private void Client_OnDevicesDiscovered(IReadOnlyList<GHubDevice> devices)
    {
        _deviceNames.Clear();

        foreach (var device in devices)
            _deviceNames[device.Id] = device.DisplayName;
    }

    private void Client_OnBatteryUpdated(GHubBattery battery)
    {
        var key = FindConfiguredKey(battery.DeviceId);

        if (key == null)
            return;

        FireAndForget(PublishBatteryAsync(key, battery));
    }

    private async Task PublishBatteryAsync(string key, GHubBattery battery)
    {
        if (_sensors.TryGetValue(key, out var sensor))
        {
            await sensor.SetAvailableAsync(true);
            await sensor.SetStateAsync(Math.Round(battery.Percentage).ToString(CultureInfo.InvariantCulture));
        }

        if (_chargingSensors.TryGetValue(key, out var charging))
        {
            await charging.SetAvailableAsync(true);
            await charging.SetStateAsync(battery.Charging);
        }
    }

    // Configured entries may name a device either way round, so try the id
    // first and then the display name it maps to.
    private string? FindConfiguredKey(string deviceId)
    {
        if (_sensors.ContainsKey(deviceId))
            return deviceId;

        return _deviceNames.TryGetValue(deviceId, out var displayName) && _sensors.ContainsKey(displayName)
            ? displayName
            : null;
    }

}