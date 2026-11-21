using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.Plugin.Base.Config;
using HomeAssistant.Agent.Plugin.Base.Config.Controls;
using HomeAssistant.Agent.Plugin.Base.Exceptions;
using HomeAssistant.Agent.Plugin.Base.Interfaces;
using HomeAssistant.Agent.Plugin.Sendspin.Config;
using HomeAssistant.Agent.Plugin.Sendspin.Player;

namespace HomeAssistant.Agent.Plugin.Sendspin;

// Makes this computer a Music Assistant player over Sendspin. The player
// itself shows up in Home Assistant through the Music Assistant integration;
// the plugin adds only whether a server is connected.
//
// Desktop only: the sound goes to the user's output device, which the service
// in session 0 has none of.
public class SendspinPlugin : BasePlugin
{
    public override string PluginCode => "sendspin-plugin";

    public override ConfigDescriptor ConfigDescriptor { get; } = new(
        new GroupControlDescriptor("Player", "Music Assistant finds the player on the network by itself.",
            new InputTextControlDescriptor("PlayerName", "Player Name",
                "Name in Music Assistant. Empty: the computer name.")
        )
    );

    public SendspinPlugin()
    {
        _status = AddFeature(new ConnectionStatusFeature("sendspin", "Music Assistant"));
    }

    public override IPluginConfig CreateConfig(ConfigValues values) => new SendspinPluginConfig
    {
        PlayerName = values.GetString("PlayerName")?.Trim() ?? string.Empty
    };

    private readonly ConnectionStatusFeature _status;
    private SendspinPlayer? _player;

    protected override async Task OnEnableAsync()
    {
        if (Config is not SendspinPluginConfig config)
            throw new PluginNotInitializedException("SendspinPluginConfig cannot be null!");

        var playerName = config.PlayerName.Length > 0 ? config.PlayerName : Environment.MachineName;

        _player = new SendspinPlayer(Context.LoggerFactory, Notifier, playerName, Info.Version ?? "0.0.0");
        _player.ConnectionChanged += Player_OnConnectionChanged;

        await _player.StartAsync();
    }

    protected override async Task OnDisableAsync()
    {
        if (_player == null)
            return;

        _player.ConnectionChanged -= Player_OnConnectionChanged;
        await _player.DisposeAsync();
        _player = null;
    }

    protected override Task PublishInitialAvailabilityAsync() =>
        PublishConnectionStateAsync(_status, _player?.IsConnected ?? false);

    private void Player_OnConnectionChanged()
    {
        FireAndForget(PublishConnectionStateAsync(_status, _player?.IsConnected ?? false));
    }
}
