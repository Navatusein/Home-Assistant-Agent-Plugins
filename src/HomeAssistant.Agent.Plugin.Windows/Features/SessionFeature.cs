using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.Plugin.Windows.Session;
using HomeAssistant.Connector.Entities;

namespace HomeAssistant.Agent.Plugin.Windows.Features;

// Whether the user's desktop is unlocked. Polled from the service through WTS,
// so it also reports the logon screen before anyone has signed in.
internal sealed class SessionFeature : PluginFeature
{
    // Device class "lock": ON reads as Unlocked (the desktop is visible),
    // OFF as Locked (the lock or logon screen).
    private readonly BinarySensorEntity _userSessionSensor = new()
    {
        Name = "User Session",
        UniqueId = "windows_session",
        BaseTopic = "windows/session",
        DeviceClass = "lock",
        DefaultState = false
    };

    private SessionMonitor? _monitor;

    public SessionFeature()
        : base("session", "User Session", "Whether the desktop is unlocked.", PluginHostMode.Service)
    {
    }

    public override IEntity[] GetEntities() => [_userSessionSensor];

    protected override async Task OnEnableAsync()
    {
        _monitor = new SessionMonitor();
        _monitor.Changed += Monitor_OnChanged;
        _monitor.Start();

        await _userSessionSensor.SetStateAsync(_monitor.IsSessionUnlocked);
    }

    protected override Task OnDisableAsync()
    {
        if (_monitor != null)
        {
            _monitor.Changed -= Monitor_OnChanged;
            _monitor.Dispose();
            _monitor = null;
        }

        return Task.CompletedTask;
    }

    private void Monitor_OnChanged()
    {
        if (_monitor != null)
            FireAndForget(_userSessionSensor.SetStateAsync(_monitor.IsSessionUnlocked));
    }
}
