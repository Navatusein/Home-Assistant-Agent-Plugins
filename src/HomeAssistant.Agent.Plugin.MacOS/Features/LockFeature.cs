using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.Plugin.MacOS.Power;
using HomeAssistant.Connector.Entities;

namespace HomeAssistant.Agent.Plugin.MacOS.Features;

// Locking needs the user's session, so unlike shutdown it stays with the
// desktop app.
internal sealed class LockFeature : PluginFeature
{
    private readonly ButtonEntity _lockButton = new()
    {
        Name = "Lock",
        UniqueId = "macos_lock",
        BaseTopic = "macos/lock",
        Icon = "mdi:lock",
        ClickHandler = () =>
        {
            PowerController.Lock();
            return Task.CompletedTask;
        }
    };

    public LockFeature()
        : base("lock", "Lock", "Button that locks the screen.", PluginHostMode.Desktop)
    {
    }

    public override IEntity[] GetEntities() => [_lockButton];
}
