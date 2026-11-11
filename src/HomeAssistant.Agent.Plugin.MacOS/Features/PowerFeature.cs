using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.Plugin.MacOS.Power;
using HomeAssistant.Connector.Entities;

namespace HomeAssistant.Agent.Plugin.MacOS.Features;

// Shutdown and restart from the service: it runs from boot as root, so the
// Mac can be turned off even while nobody is logged in.
internal sealed class PowerFeature : PluginFeature
{
    private readonly ButtonEntity _shutdownButton = new()
    {
        Name = "Shutdown",
        UniqueId = "macos_power_shutdown",
        BaseTopic = "macos/power/shutdown",
        Icon = "mdi:power",
        ClickHandler = () =>
        {
            PowerController.Shutdown();
            return Task.CompletedTask;
        }
    };

    private readonly ButtonEntity _restartButton = new()
    {
        Name = "Restart",
        UniqueId = "macos_power_restart",
        BaseTopic = "macos/power/restart",
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
