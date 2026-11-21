using System.Text.RegularExpressions;
using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.Plugin.Base.Config;
using HomeAssistant.Agent.Plugin.Base.Config.Controls;
using HomeAssistant.Agent.Plugin.Base.Enums;
using HomeAssistant.Agent.Plugin.Base.Exceptions;
using HomeAssistant.Agent.Plugin.Base.Interfaces;
using HomeAssistant.Agent.Plugin.Ntfy.Config;
using HomeAssistant.Agent.Plugin.Ntfy.Ntfy;
using HomeAssistant.Connector.Entities;
using Microsoft.Extensions.Logging;

namespace HomeAssistant.Agent.Plugin.Ntfy;

// Shows the messages of ntfy topics (ntfy.sh or a server of one's own) as
// notifications on this computer. Home Assistant gets whether the plugin is
// subscribed, and a Do Not Disturb switch that holds the notifications back
// — for an automation to turn on during a game or a call.
//
// Desktop only: the service has nobody to show a notification to.
public partial class NtfyPlugin : BasePlugin
{
    public override string PluginCode => "ntfy-plugin";

    private const string DoNotDisturbKey = "do-not-disturb";

    public override ConfigDescriptor ConfigDescriptor { get; } = new(
        new GroupControlDescriptor("Subscription", null,
            new InputTextControlDescriptor("Server", "Server",
                "Address of the ntfy server.",
                defaultValue: NtfyPluginConfig.DefaultServer),
            new ComplexListControlDescriptor("Topics", "Topics", "One row per topic.",
                new InputTextControlDescriptor("Topic", "Topic", "Name of the topic on the server.")),
            new InputTextControlDescriptor("Token", "Access Token",
                "For topics that need one (tk_…).",
                type: InputTextControlTypesEnum.Password),
            new SelectControlDescriptor("MinPriority", "Minimum Priority",
                "Messages of a lower priority are not shown.",
                [
                    new("1", "Min (1)"),
                    new("2", "Low (2)"),
                    new("3", "Default (3)"),
                    new("4", "High (4)"),
                    new("5", "Max (5)"),
                ],
                defaultValue: "1"),
            new SwitchControlDescriptor("HttpActions", "HTTP Buttons",
                "Buttons that send a request from this computer, as the message sets it.")
        )
    );

    // Without a topic there is nothing to subscribe to.
    protected override bool CanEnable => Config is NtfyPluginConfig { Topics.Count: > 0 };

    public NtfyPlugin()
    {
        // Its state is the one stored, published once it is read (OnEnableAsync).
        _doNotDisturbSwitch = new SwitchEntity
        {
            Name = "ntfy Do Not Disturb",
            UniqueId = "ntfy_do_not_disturb",
            BaseTopic = "ntfy/do_not_disturb",
            Icon = "mdi:bell-off",
            PublishDefaultState = false,
            ChangeStateHandler = DoNotDisturbSwitch_ChangeStateHandler
        };

        AddFeature(new EntityFeature("do-not-disturb", "Do Not Disturb",
            "Hold the notifications back.", PluginHostMode.Desktop, _doNotDisturbSwitch));

        _status = AddFeature(new ConnectionStatusFeature("ntfy", "ntfy"));
    }

    public override IPluginConfig CreateConfig(ConfigValues values) => new NtfyPluginConfig
    {
        Server = NormalizeServer(values.GetString("Server")),
        Topics = CleanTopics(values.GetList<NtfyTopicEntry>("Topics")),
        Token = values.GetString("Token")?.Trim() ?? string.Empty,
        MinPriority = Math.Clamp(values.GetInt("MinPriority", 1), 1, 5),
        HttpActions = values.GetBool("HttpActions", false)
    };

    // "https://ntfy.sh/" -> "https://ntfy.sh"; a bare host gets https.
    internal static string NormalizeServer(string? server)
    {
        var value = server?.Trim().TrimEnd('/') ?? string.Empty;

        if (value.Length == 0)
            return NtfyPluginConfig.DefaultServer;

        return value.Contains("://", StringComparison.Ordinal) ? value : "https://" + value;
    }

    // The rows of the list, trimmed, each topic once. ntfy topic names are
    // letters, digits, "-" and "_", up to 64 long; a row with anything else
    // (or nothing) is dropped.
    internal static IReadOnlyList<string> CleanTopics(IEnumerable<NtfyTopicEntry>? entries) =>
        (entries ?? [])
            .Select(entry => entry.Topic?.Trim() ?? string.Empty)
            .Where(topic => TopicName().IsMatch(topic))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    [GeneratedRegex("^[-_A-Za-z0-9]{1,64}$")]
    private static partial Regex TopicName();

    private readonly SwitchEntity _doNotDisturbSwitch;
    private readonly ConnectionStatusFeature _status;

    private NtfyClient? _client;
    private NtfyHttpAction? _httpActions;
    private ILogger<NtfyPlugin>? _logger;
    private volatile bool _doNotDisturb;
    private int _minPriority = 1;

    protected override async Task OnEnableAsync()
    {
        if (Config is not NtfyPluginConfig config)
            throw new PluginNotInitializedException("NtfyPluginConfig cannot be null!");

        _logger = CreateLogger<NtfyPlugin>();
        _minPriority = config.MinPriority;
        _httpActions = config.HttpActions ? new NtfyHttpAction(CreateLogger<NtfyHttpAction>()) : null;
        _doNotDisturb = await Storage.GetAsync(DoNotDisturbKey, false);

        await _doNotDisturbSwitch.SetStateAsync(_doNotDisturb);

        _client = new NtfyClient(config.Server, config.Topics, config.Token,
            $"HomeAssistantAgent/{Context.AgentVersion}", CreateLogger<NtfyClient>());

        _client.ConnectionChanged += Client_OnConnectionChanged;
        _client.MessageReceived += Client_OnMessageReceived;
        _client.Start();
    }

    protected override Task OnDisableAsync()
    {
        if (_client != null)
        {
            _client.ConnectionChanged -= Client_OnConnectionChanged;
            _client.MessageReceived -= Client_OnMessageReceived;
            _client.Dispose();
            _client = null;
        }

        _httpActions?.Dispose();
        _httpActions = null;

        return Task.CompletedTask;
    }

    // Do Not Disturb works whether or not ntfy is reachable: only the status
    // sensor follows the connection.
    protected override async Task PublishInitialAvailabilityAsync()
    {
        await SetEntitiesAvailabilityAsync(true);
        await _status.Sensor.SetStateAsync(_client?.IsConnected ?? false);
    }

    private void Client_OnConnectionChanged()
    {
        FireAndForget(_status.Sensor.SetStateAsync(_client?.IsConnected ?? false));
    }

    private void Client_OnMessageReceived(NtfyMessage message)
    {
        if (message.EffectivePriority < _minPriority)
            return;

        if (_doNotDisturb)
        {
            _logger?.LogDebug("Held back during Do Not Disturb: {Topic}/{Id}", message.Topic, message.Id);
            return;
        }

        Notifier.Show(message.ToNotification(_httpActions == null ? null : _httpActions.For));
    }

    private async Task<bool> DoNotDisturbSwitch_ChangeStateHandler(bool doNotDisturb)
    {
        _doNotDisturb = doNotDisturb;
        await Storage.SetAsync(DoNotDisturbKey, doNotDisturb);
        return doNotDisturb;
    }
}
