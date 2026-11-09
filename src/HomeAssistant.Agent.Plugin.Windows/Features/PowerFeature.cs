using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.Plugin.Windows.Power;
using HomeAssistant.Connector.Entities;

namespace HomeAssistant.Agent.Plugin.Windows.Features;

// Shutdown and restart from the service: it runs from boot, so the PC can be
// turned off even while nobody is signed in.
internal sealed class PowerFeature : PluginFeature
{
    private readonly ButtonEntity _shutdownButton = new()
    {
        Name = "Shutdown PC",
        UniqueId = "windows_power_shutdown",
        BaseTopic = "windows/power/shutdown",
        Icon = "mdi:power",
        ClickHandler = () =>
        {
            PowerController.Shutdown();
            return Task.CompletedTask;
        }
    };

    private readonly ButtonEntity _restartButton = new()
    {
        Name = "Restart PC",
        UniqueId = "windows_power_restart",
        BaseTopic = "windows/power/restart",
        Icon = "mdi:restart",
        ClickHandler = () =>
        {
            PowerController.Restart();
            return Task.CompletedTask;
        }
    };

    public PowerFeature()
        : base("power", "Power", "Shutdown and restart buttons.", PluginHostMode.Service)
    {
    }

    public override IEntity[] GetEntities() => [_shutdownButton, _restartButton];
}
