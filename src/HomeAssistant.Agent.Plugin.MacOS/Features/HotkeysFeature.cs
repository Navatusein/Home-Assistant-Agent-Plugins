using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.Plugin.Base.Config.Controls;
using HomeAssistant.Agent.Plugin.Base.Interfaces;
using HomeAssistant.Agent.Plugin.MacOS.Config;
using HomeAssistant.Agent.Plugin.MacOS.Keyboard;
using HomeAssistant.Connector.Entities;

namespace HomeAssistant.Agent.Plugin.MacOS.Features;

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
                new InputTextControlDescriptor("Keys", "Keys", "Key combination (e.g. Cmd+Option+Esc).")
            ),
        ];
    }

    public override IEntity[] GetEntities() => [.. _buttons];

    protected override void OnConfigured(IPluginConfig config)
    {
        _buttons.Clear();

        foreach (var hotkey in ((MacOsPluginConfig) config).Hotkeys)
        {
            var slug = Slug.ToObjectId(hotkey.Name);
            var keys = hotkey.Keys;

            _buttons.Add(new ButtonEntity
            {
                Name = $"Hotkey {hotkey.Name}",
                UniqueId = $"macos_hotkeys_{slug}",
                BaseTopic = $"macos/hotkeys/{slug}",
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
