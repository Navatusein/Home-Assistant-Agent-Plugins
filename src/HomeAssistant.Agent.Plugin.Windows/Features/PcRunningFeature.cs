using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Connector.Entities;

namespace HomeAssistant.Agent.Plugin.Windows.Features;

// "Is the PC on": the sensor's state topic is the service session's
// availability topic, so the broker's last will flips it to "off" when the PC
// dies — it reflects the machine being up rather than going unavailable with it.
internal sealed class PcRunningFeature : PluginFeature
{
    private readonly AvailabilityBinarySensorEntity _pcRunningSensor = new()
    {
        Name = "PC Running",
        UniqueId = "windows_pc_running",
        BaseTopic = "windows/pc-running",
        Icon = "mdi:desktop-tower",
        DeviceClass = "running"
    };

    public PcRunningFeature()
        : base("pc-running", "PC Running", "Whether the PC is on.", PluginHostMode.Service)
    {
    }

    public override IEntity[] GetEntities() => [_pcRunningSensor];
}
