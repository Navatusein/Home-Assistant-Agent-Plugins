using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.Plugin.Base.Config.Controls;
using HomeAssistant.Agent.Plugin.Base.Interfaces;
using HomeAssistant.Agent.Plugin.Windows.Config;
using HomeAssistant.Agent.Plugin.Windows.Keyboard;
using HomeAssistant.Connector.Entities;

namespace HomeAssistant.Agent.Plugin.Windows.Features;

// A button per configured key combination.
internal sealed class HotkeysFeature : PluginFeature
{
    private readonly List<ButtonEntity> _buttons = [];

    public HotkeysFeature()
        : base("hotkeys", "Hotkeys", "Buttons that send key combinations.", PluginHostMode.Desktop)
    {
        Controls =
        [
            new ComplexListControlDescriptor("Hotkeys", "Hotkeys", "One button per hotkey.",
                new InputTextControlDescriptor("Name", "Name", "Name shown in Home Assistant."),
                new InputTextControlDescriptor("Keys", "Keys", "Key combination (e.g. Ctrl+Shift+Esc).")
            ),
        ];
    }

    public override IEntity[] GetEntities() => [.. _buttons];

    protected override void OnConfigured(IPluginConfig config)
    {
        _buttons.Clear();

        foreach (var hotkey in ((WindowsPluginConfig) config).Hotkeys)
        {
            var slug = Slug.ToObjectId(hotkey.Name);
            var keys = hotkey.Keys;

            _buttons.Add(new ButtonEntity
            {
                Name = $"Hotkey {hotkey.Name}",
                UniqueId = $"windows_hotkeys_{slug}",
                BaseTopic = $"windows/hotkeys/{slug}",
                Icon = "mdi:keyboard",
                ClickHandler = () =>
                {
                    HotkeySender.Send(keys);
                    return Task.CompletedTask;
                }
            });
        }
    }
}
