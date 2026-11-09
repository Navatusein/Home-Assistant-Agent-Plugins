using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Connector.Entities;

namespace HomeAssistant.Agent.Plugin.Windows.Features;

// Whether the desktop app runs: the sensor mirrors the app's own MQTT
// availability (online on connect, offline via last will or the clean-exit
// publish) — no process watching needed.
internal sealed class AgentRunningFeature : PluginFeature
{
    private readonly AvailabilityBinarySensorEntity _agentRunningSensor = new()
    {
        Name = "Agent Running",
        UniqueId = "windows_agent_running",
        BaseTopic = "windows/agent-running",
        Icon = "mdi:robot",
        DeviceClass = "running"
    };

    public AgentRunningFeature()
        : base("agent-running", "Agent Running", "Whether the desktop app is running.", PluginHostMode.Desktop)
    {
    }

    public override IEntity[] GetEntities() => [_agentRunningSensor];
}
