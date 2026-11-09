using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.Plugin.Base.Config;
using HomeAssistant.Agent.Plugin.Base.Config.Controls;
using HomeAssistant.Agent.Plugin.Base.Exceptions;
using HomeAssistant.Agent.Plugin.Base.Interfaces;
using HomeAssistant.Agent.Plugin.Teams.Config;
using HomeAssistant.Agent.Plugin.Teams.Teams;
using HomeAssistant.Connector.Entities;

namespace HomeAssistant.Agent.Plugin.Teams;

public class TeamsPlugin : BasePlugin
{
    private const string TokenStorageKey = "api-token";

    // Identifies the plugin to Teams: it is shown in the pairing prompt and
    // ties the issued token to this app, so it must stay stable across
    // renames — changing it invalidates an existing pairing.
    private const string TeamsAppName = "HomeAssistantAgent";

    public override string PluginCode => "teams-plugin";

    public override ConfigDescriptor ConfigDescriptor { get; } = new(
        new GroupControlDescriptor("Teams", "Requires the third-party API to be enabled in Teams.",
            new InputTextControlDescriptor("Host", "Host",
                "Host of the Teams local API.",
                defaultValue: "localhost")
        )
    );

    public override IPluginConfig CreateConfig(ConfigValues values)
    {
        return new TeamsPluginConfig
        {
            Host = values.GetString("Host") ?? "localhost"
        };
    }

    private TeamsLocalClient? _client;

    private readonly SwitchEntity _muteSwitch;
    private readonly SwitchEntity _cameraSwitch;
    private readonly BinarySensorEntity _meetingSensor;
    private readonly ConnectionStatusFeature _status;

    public TeamsPlugin()
    {
        _muteSwitch = new SwitchEntity
        {
            Name = "Teams Mute",
            UniqueId = "teams_meeting_controls_mute",
            BaseTopic = "teams/meeting-controls/mute",
            Icon = "mdi:microphone-off",
            DefaultState = false,
            ChangeStateHandler = MuteSwitch_ChangeStateHandler
        };

        _cameraSwitch = new SwitchEntity
        {
            Name = "Teams Camera",
            UniqueId = "teams_meeting_controls_camera",
            BaseTopic = "teams/meeting-controls/camera",
            Icon = "mdi:video",
            DefaultState = false,
            ChangeStateHandler = CameraSwitch_ChangeStateHandler
        };

        _meetingSensor = new BinarySensorEntity
        {
            Name = "Teams Meeting",
            UniqueId = "teams_meeting",
            BaseTopic = "teams/meeting",
            Icon = "mdi:account-group",
            DefaultState = false
        };

        AddFeature(new EntityFeature("meeting-controls", "Meeting Controls",
            "Mute and camera in a meeting.", PluginHostMode.Desktop, _muteSwitch, _cameraSwitch));

        AddFeature(new EntityFeature("meeting", "Meeting",
            "Whether you are in a meeting.", PluginHostMode.Desktop, _meetingSensor));

        _status = AddFeature(new ConnectionStatusFeature("teams", "Teams"));
    }

    protected override async Task OnEnableAsync()
    {
        if (Config is not TeamsPluginConfig config)
            throw new PluginNotInitializedException("TeamsPluginConfig cannot be null!");

        var token = await Storage.GetAsync<string>(TokenStorageKey);

        _client = new TeamsLocalClient(config.Host, token, TeamsAppName,
            typeof(TeamsPlugin).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
            CreateLogger<TeamsLocalClient>());

        _client.ConnectionChanged += Client_OnConnectionChanged;
        _client.MeetingStateChanged += Client_OnMeetingStateChanged;
        _client.TokenRefreshed += Client_OnTokenRefreshed;
        _client.Start();
    }

    protected override Task OnDisableAsync()
    {
        if (_client != null)
        {
            _client.ConnectionChanged -= Client_OnConnectionChanged;
            _client.MeetingStateChanged -= Client_OnMeetingStateChanged;
            _client.TokenRefreshed -= Client_OnTokenRefreshed;
            _client.Dispose();
            _client = null;
        }

        return Task.CompletedTask;
    }

    // The controls are only usable while the local Teams API is reachable —
    // their availability follows the connection instead of the enable step.
    // The status sensor stays available so the connection state is visible.
    protected override Task PublishInitialAvailabilityAsync() =>
        PublishConnectionStateAsync(_status, _client?.IsConnected ?? false);

    private void Client_OnConnectionChanged()
    {
        FireAndForget(PublishConnectionStateAsync(_status, _client?.IsConnected ?? false));
    }

    private void Client_OnMeetingStateChanged()
    {
        var state = _client?.MeetingState;

        FireAndForget(_muteSwitch.SetStateAsync(state?.IsMuted ?? false));
        FireAndForget(_cameraSwitch.SetStateAsync(state?.IsVideoOn ?? false));
        FireAndForget(_meetingSensor.SetStateAsync(state is { IsInMeeting: true }));
    }

    private Task Client_OnTokenRefreshed(string token)
    {
        return token.Length == 0
            ? Storage.RemoveAsync(TokenStorageKey)
            : Storage.SetAsync(TokenStorageKey, token);
    }

    private async Task<bool> MuteSwitch_ChangeStateHandler(bool muted)
    {
        var state = _client?.MeetingState;

        // Outside a meeting there is nothing to mute — keep the known state.
        if (_client == null || state is not { IsInMeeting: true })
            return state?.IsMuted ?? false;

        if (muted != state.IsMuted)
            await _client.ToggleMuteAsync();

        // Optimistic: the next meetingUpdate corrects the state if Teams
        // refused the command (e.g. the plugin is not paired yet).
        return muted;
    }

    private async Task<bool> CameraSwitch_ChangeStateHandler(bool cameraOn)
    {
        var state = _client?.MeetingState;

        if (_client == null || state is not { IsInMeeting: true })
            return state?.IsVideoOn ?? false;

        if (cameraOn != state.IsVideoOn)
            await _client.ToggleVideoAsync();

        return cameraOn;
    }
}
