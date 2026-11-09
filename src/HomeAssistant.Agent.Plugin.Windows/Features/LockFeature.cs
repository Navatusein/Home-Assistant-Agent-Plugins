using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.Plugin.Windows.Power;
using HomeAssistant.Connector.Entities;

namespace HomeAssistant.Agent.Plugin.Windows.Features;

// Locking needs the interactive session, so unlike shutdown it stays with the
// desktop app.
internal sealed class LockFeature : PluginFeature
{
    private readonly ButtonEntity _lockButton = new()
    {
        Name = "Lock PC",
        UniqueId = "windows_lock",
        BaseTopic = "windows/lock",
        Icon = "mdi:lock",
        ClickHandler = () =>
        {
            PowerController.Lock();
            return Task.CompletedTask;
        }
    };

    public LockFeature()
        : base("lock", "Lock", "Button that locks the PC.", PluginHostMode.Desktop)
    {
    }

    public override IEntity[] GetEntities() => [_lockButton];
}
