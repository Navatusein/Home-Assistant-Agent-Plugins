using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Connector.Entities;

namespace HomeAssistant.Agent.Plugin.MacOS.Features;

// "Is the Mac on": the sensor's state topic is the service session's
// availability topic, so the broker's last will flips it to "off" when the Mac
// goes down — it reflects the machine being up rather than going unavailable
// with it.
internal sealed class ComputerRunningFeature : PluginFeature
{
    private readonly AvailabilityBinarySensorEntity _computerRunningSensor = new()
    {
        Name = "Computer Running",
        UniqueId = "macos_computer_running",
        BaseTopic = "macos/computer-running",
        Icon = "mdi:laptop",
        DeviceClass = "running"
    };

    public ComputerRunningFeature()
        : base("computer-running", "Computer Running", "Whether the computer is on.", PluginHostMode.Service)
    {
    }

    public override IEntity[] GetEntities() => [_computerRunningSensor];
}
