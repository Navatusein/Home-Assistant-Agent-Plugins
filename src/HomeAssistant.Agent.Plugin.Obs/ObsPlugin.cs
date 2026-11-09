using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.Plugin.Base.Config;
using HomeAssistant.Agent.Plugin.Base.Config.Controls;
using HomeAssistant.Agent.Plugin.Base.Enums;
using HomeAssistant.Agent.Plugin.Base.Exceptions;
using HomeAssistant.Agent.Plugin.Base.Interfaces;
using HomeAssistant.Agent.Plugin.Obs.Config;
using HomeAssistant.Agent.Plugin.Obs.Obs;
using HomeAssistant.Connector.Entities;

namespace HomeAssistant.Agent.Plugin.Obs;

// Starts and stops the OBS Studio recording and switches the program scene,
// over the WebSocket server OBS ships with.
public class ObsPlugin : BasePlugin
{
    public override string PluginCode => "obs-plugin";

    private const int DefaultPort = 4455;

    public override ConfigDescriptor ConfigDescriptor { get; } = new(
        new GroupControlDescriptor("Connection",
            "Requires the WebSocket server to be enabled in OBS (Tools -> WebSocket Server Settings).",
            new InputTextControlDescriptor("Host", "Host",
                "Host running OBS. Empty leaves the plugin off.",
                defaultValue: "localhost"),
            new InputTextControlDescriptor("Port", "Port",
                "Port of the OBS WebSocket server.",
                defaultValue: "4455",
                type: InputTextControlTypesEnum.Number),
            new InputTextControlDescriptor("Password", "Password",
                "Server password from the OBS WebSocket settings.",
                type: InputTextControlTypesEnum.Password)
        )
    );

    // Without a host there is nothing to connect to — leave the plugin
    // disabled instead of registering entities that can never work.
    protected override bool CanEnable =>
        Config is ObsPluginConfig config && !string.IsNullOrWhiteSpace(config.Host);

    public ObsPlugin()
    {
        _recordingSwitch = new SwitchEntity
        {
            Name = "OBS Recording",
            UniqueId = "obs_recording",
            BaseTopic = "obs/recording",
            Icon = "mdi:record-rec",
            DefaultState = false,
            ChangeStateHandler = RecordingSwitch_ChangeStateHandler
        };

        // The options are whatever OBS reports; until it is connected the
        // select has none and no state to show (see ApplyScenesAsync).
        _sceneSelect = new SelectEntity
        {
            Name = "OBS Scene",
            UniqueId = "obs_scenes",
            BaseTopic = "obs/scenes",
            Icon = "mdi:movie-open-play",
            Options = [],
            ChangeStateHandler = SceneSelect_ChangeStateHandler
        };

        AddFeature(new EntityFeature("recording", "Recording",
            "Start and stop the recording.", PluginHostMode.Desktop, _recordingSwitch));

        AddFeature(new EntityFeature("scenes", "Scenes",
            "Switch the program scene.", PluginHostMode.Desktop, _sceneSelect));

        _status = AddFeature(new ConnectionStatusFeature("obs", "OBS"));
    }

    public override IPluginConfig? CreateConfig(ConfigValues values)
    {
        var host = values.GetString("Host");

        if (host == null)
            return null;

        return new ObsPluginConfig
        {
            Host = host,
            Port = values.GetInt("Port", DefaultPort),
            Password = values.GetString("Password") ?? string.Empty
        };
    }

    private ObsClient? _client;

    private readonly SwitchEntity _recordingSwitch;
    private readonly SelectEntity _sceneSelect;
    private readonly ConnectionStatusFeature _status;

    // Last state OBS reported, so a refused command can fall back to it
    // instead of leaving Home Assistant showing the requested one.
    private bool _recording;
    private string? _currentScene;

    protected override Task OnEnableAsync()
    {
        if (Config is not ObsPluginConfig config)
            throw new PluginNotInitializedException("ObsPluginConfig cannot be null!");

        _client = new ObsClient(config.Host, config.Port, config.Password, CreateLogger<ObsClient>());

        _client.ConnectionChanged += Client_OnConnectionChanged;
        _client.ScenesChanged += Client_OnScenesChanged;
        _client.CurrentSceneChanged += Client_OnCurrentSceneChanged;
        _client.RecordingChanged += Client_OnRecordingChanged;
        _client.Start();

        return Task.CompletedTask;
    }

    protected override Task OnDisableAsync()
    {
        if (_client != null)
        {
            _client.ConnectionChanged -= Client_OnConnectionChanged;
            _client.ScenesChanged -= Client_OnScenesChanged;
            _client.CurrentSceneChanged -= Client_OnCurrentSceneChanged;
            _client.RecordingChanged -= Client_OnRecordingChanged;
            _client.Dispose();
            _client = null;
        }

        _currentScene = null;

        return Task.CompletedTask;
    }

    // The controls only do anything while OBS is reachable — their
    // availability follows the connection instead of the enable step. The
    // status sensor stays available so the connection state is visible.
    protected override Task PublishInitialAvailabilityAsync() =>
        PublishConnectionStateAsync(_status, _client?.IsConnected ?? false);

    private void Client_OnConnectionChanged()
    {
        FireAndForget(PublishConnectionStateAsync(_status, _client?.IsConnected ?? false));
    }

    private void Client_OnScenesChanged(IReadOnlyList<string> scenes)
    {
        FireAndForget(ApplyScenesAsync(scenes));
    }

    // The scene list lives in the select's discovery config, so a scene added
    // or renamed in OBS means re-publishing that config. The entity itself is
    // kept: it already owns the command subscription.
    private async Task ApplyScenesAsync(IReadOnlyList<string> scenes)
    {
        if (_sceneSelect.Options.SequenceEqual(scenes, StringComparer.Ordinal))
            return;

        _sceneSelect.Options = [.. scenes];

        if (Client != null)
            await Client.RefreshDiscoveryAsync(_sceneSelect);

        // Home Assistant drops a state that is not one of the options, so the
        // active scene is announced again against the new list.
        if (_currentScene != null && scenes.Contains(_currentScene, StringComparer.Ordinal))
            await _sceneSelect.SetStateAsync(_currentScene);
    }

    private void Client_OnCurrentSceneChanged(string sceneName)
    {
        _currentScene = sceneName;

        // A scene the select does not know yet is published by ApplyScenesAsync
        // once its options carry it.
        if (_sceneSelect.Options.Contains(sceneName, StringComparer.Ordinal))
            FireAndForget(_sceneSelect.SetStateAsync(sceneName));
    }

    private void Client_OnRecordingChanged(bool recording)
    {
        _recording = recording;
        FireAndForget(_recordingSwitch.SetStateAsync(recording));
    }

    private async Task<bool> RecordingSwitch_ChangeStateHandler(bool recording)
    {
        if (_client is not { IsConnected: true } client)
            return _recording;

        // Optimistic: the RecordStateChanged event corrects the switch if OBS
        // needs a moment, and a refused command keeps the known state.
        return await client.SetRecordingAsync(recording) ? recording : _recording;
    }

    private async Task<string> SceneSelect_ChangeStateHandler(string sceneName)
    {
        if (_client is not { IsConnected: true } client)
            return _currentScene ?? sceneName;

        return await client.SetCurrentSceneAsync(sceneName) ? sceneName : _currentScene ?? sceneName;
    }
}