using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.Plugin.Base.Config;
using HomeAssistant.Agent.Plugin.Base.Config.Controls;
using HomeAssistant.Agent.Plugin.Base.Enums;
using HomeAssistant.Agent.Plugin.Base.Exceptions;
using HomeAssistant.Agent.Plugin.Base.Interfaces;
using HomeAssistant.Agent.Plugin.Discord.Contracts.Responses;
using HomeAssistant.Agent.Plugin.Discord.Discord;
using HomeAssistant.Agent.Plugin.Discord.Enums;
using HomeAssistant.Connector.Entities;

namespace HomeAssistant.Agent.Plugin.Discord;

public class DiscordPlugin: BasePlugin
{
    public override string PluginCode => "discord-plugin";

    public override ConfigDescriptor ConfigDescriptor { get; } = new(
        new GroupControlDescriptor("Discord Auth", "Taken from the Discord developer portal.",
            new InputTextControlDescriptor("ClientId", "Client Id",
                "Client id of the Discord application."),
            new InputTextControlDescriptor("ClientSecret", "Client Secret",
                "Client secret of the Discord application.",
                type: InputTextControlTypesEnum.Password)
        )
    );

    // Without credentials the plugin cannot talk to Discord at all — refuse
    // to enable so the entities never register or light up in Home Assistant.
    protected override bool CanEnable =>
        Config is DiscordPluginConfig config
        && !string.IsNullOrWhiteSpace(config.ClientId)
        && !string.IsNullOrWhiteSpace(config.ClientSecret);

    public override IPluginConfig? CreateConfig(ConfigValues values)
    {
        var clientId = values.GetString("ClientId");
        var clientSecret = values.GetString("ClientSecret");

        if (clientId == null || clientSecret == null)
            return null;

        return new DiscordPluginConfig
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
        };
    }

    private const string NotInVoiceChannel = "Not in voice channel";

    private DiscordClient? _client;

    // Last state Discord reported, so a refused command can fall back to it
    // instead of leaving Home Assistant showing the requested one.
    private bool _muted;
    private bool _deafened;

    private readonly SwitchEntity _discordMuteSwitch;
    private readonly SwitchEntity _discordDeafenSwitch;
    private readonly SensorEntity _discordVoiceChannelIdSensor;
    private readonly BinarySensorEntity _discordInVoiceChannelSensor;
    private readonly ButtonEntity _discordLeaveChannelButton;
    private readonly ConnectionStatusFeature _status;

    public DiscordPlugin()
    {
        _discordMuteSwitch = new SwitchEntity
        {
            Name = "Discord Mute",
            UniqueId = "discord_voice_controls_mute",
            BaseTopic = "discord/voice-controls/mute",
            DefaultState = false,
            ChangeStateHandler = DiscordMuteSwitch_ChangeStateHandler
        };

        _discordDeafenSwitch = new SwitchEntity
        {
            Name = "Discord Deafen",
            UniqueId = "discord_voice_controls_deafen",
            BaseTopic = "discord/voice-controls/deafen",
            DefaultState = false,
            ChangeStateHandler = DiscordDeafenSwitch_ChangeStateHandler
        };

        _discordVoiceChannelIdSensor = new SensorEntity
        {
            Name = "Discord Voice Channel Id",
            UniqueId = "discord_voice_channel_id",
            BaseTopic = "discord/voice-channel/id",
            DefaultState = NotInVoiceChannel,
        };

        _discordInVoiceChannelSensor = new BinarySensorEntity
        {
            Name = "Discord Voice Channel",
            UniqueId = "discord_voice_channel_active",
            BaseTopic = "discord/voice-channel/active",
            Icon = "mdi:headset",
            DefaultState = false,
        };

        _discordLeaveChannelButton = new ButtonEntity()
        {
            Name = "Discord Leave Channel",
            UniqueId = "discord_voice_controls_leave",
            BaseTopic = "discord/voice-controls/leave",
            ClickHandler = DiscordLeaveChannelButton_ClickHandler
        };

        // One connection feeds them all (see OnEnableAsync); the features only
        // decide which groups of entities exist in Home Assistant.
        AddFeature(new EntityFeature("voice-controls", "Voice Controls",
            "Mute, deafen and leave the voice channel.", PluginHostMode.Desktop,
            _discordMuteSwitch, _discordDeafenSwitch, _discordLeaveChannelButton));

        AddFeature(new EntityFeature("voice-channel", "Voice Channel",
            "Whether you are in a voice channel, and which.", PluginHostMode.Desktop,
            _discordInVoiceChannelSensor, _discordVoiceChannelIdSensor));

        _status = AddFeature(new ConnectionStatusFeature("discord", "Discord"));
    }

    // The client waits for Discord to start, connects, logs in and reconnects
    // on its own; the entities follow its connection.
    protected override Task OnEnableAsync()
    {
        if (Config is not DiscordPluginConfig config)
            throw new PluginNotInitializedException("DiscordPluginConfig cannot be null!");

        _client = new DiscordClient(config.ClientId, config.ClientSecret, Storage, CreateLogger<DiscordClient>());

        _client.ConnectionChanged += Client_OnConnectionChanged;
        _client.VoiceSettingsUpdated += Client_OnVoiceSettingsUpdated;
        _client.VoiceChannelSelected += Client_OnVoiceChannelSelected;
        _client.Start();

        return Task.CompletedTask;
    }

    protected override Task OnDisableAsync()
    {
        if (_client != null)
        {
            _client.ConnectionChanged -= Client_OnConnectionChanged;
            _client.VoiceSettingsUpdated -= Client_OnVoiceSettingsUpdated;
            _client.VoiceChannelSelected -= Client_OnVoiceChannelSelected;
            _client.Dispose();
            _client = null;
        }

        return Task.CompletedTask;
    }

    // The controls only do anything while Discord is connected — their
    // availability follows the connection instead of the enable step. The
    // status sensor stays available so the connection state is visible.
    protected override Task PublishInitialAvailabilityAsync() =>
        PublishConnectionStateAsync(_status, _client?.IsConnected ?? false);

    private void Client_OnConnectionChanged()
    {
        var connected = _client?.IsConnected ?? false;

        FireAndForget(PublishConnectionStateAsync(_status, connected));

        if (connected)
        {
            FireAndForget(PublishCurrentVoiceChannelAsync());
            FireAndForget(PublishCurrentVoiceSettingsAsync());
        }
    }

    // VOICE_SETTINGS_UPDATE is pushed only on changes, so mute / deafen would
    // stay at their default until the first toggle even if the user is already
    // muted at connect time — query the current settings once and publish them.
    private async Task PublishCurrentVoiceSettingsAsync()
    {
        if (_client is not { IsConnected: true } client)
            return;

        var payload = await client.SendCommandAsync(DiscordCommandEnum.GetVoiceSettings);

        if (payload?.DataAs<VoiceSettingsResponse>() is { } settings)
            Client_OnVoiceSettingsUpdated(settings);
    }

    // The RPC pushes VOICE_CHANNEL_SELECT only on changes, so when the user
    // already sits in a channel at connect time the sensors would show the
    // default until the first switch — query the current channel once.
    private async Task PublishCurrentVoiceChannelAsync()
    {
        if (_client is not { IsConnected: true } client)
            return;

        // Unlike the event, GET_SELECTED_VOICE_CHANNEL answers with the full
        // channel object ("id"), or null data when not in a voice channel.
        var payload = await client.SendCommandAsync(DiscordCommandEnum.GetSelectedVoiceChannel);

        if (payload == null)
            return;

        var channelId = payload.GetDataString("id");

        await _discordVoiceChannelIdSensor.SetStateAsync(channelId ?? NotInVoiceChannel);
        await _discordInVoiceChannelSensor.SetStateAsync(channelId != null);
    }

    private void Client_OnVoiceSettingsUpdated(VoiceSettingsResponse response)
    {
        _muted = response.IsMute || response.IsDeaf;
        _deafened = response.IsDeaf;

        FireAndForget(_discordMuteSwitch.SetStateAsync(_muted));
        FireAndForget(_discordDeafenSwitch.SetStateAsync(_deafened));
    }

    private void Client_OnVoiceChannelSelected(ChannelSelectResponse response)
    {
        FireAndForget(_discordVoiceChannelIdSensor.SetStateAsync(response.Id ?? NotInVoiceChannel));
        FireAndForget(_discordInVoiceChannelSensor.SetStateAsync(response.Id != null));
    }

    // Optimistic: VOICE_SETTINGS_UPDATE corrects the switch, and a command
    // that did not go through keeps the known state.
    private async Task<bool> DiscordMuteSwitch_ChangeStateHandler(bool muted)
    {
        if (_client is not { IsConnected: true } client)
            return _muted;

        return await client.SendCommandAsync(DiscordCommandEnum.SetVoiceSettings, new { mute = muted }) is { IsError: false }
            ? muted
            : _muted;
    }

    private async Task<bool> DiscordDeafenSwitch_ChangeStateHandler(bool deafened)
    {
        if (_client is not { IsConnected: true } client)
            return _deafened;

        return await client.SendCommandAsync(DiscordCommandEnum.SetVoiceSettings, new { deaf = deafened }) is { IsError: false }
            ? deafened
            : _deafened;
    }

    private async Task DiscordLeaveChannelButton_ClickHandler()
    {
        if (_client is not { IsConnected: true } client)
            return;

        await client.SendCommandAsync(DiscordCommandEnum.SelectVoiceChannel, new { channel_id = (string?) null });
    }
}
