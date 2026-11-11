using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.Plugin.Base.Config.Controls;
using HomeAssistant.Agent.Plugin.Base.Enums;
using HomeAssistant.Agent.Plugin.Base.Interfaces;
using HomeAssistant.Agent.Plugin.MacOS.Clipboard;
using HomeAssistant.Agent.Plugin.MacOS.Config;
using HomeAssistant.Connector.Entities;

namespace HomeAssistant.Agent.Plugin.MacOS.Features;

// Text slots filled from Home Assistant and pasted into the focused app, plus
// how the paste is done, and a notify entity that puts text on the clipboard.
internal sealed class ClipboardFeature : PluginFeature
{
    private readonly ClipboardPaster _paster = new();
    private readonly List<ClipboardSlot> _slots = [];

    private SelectEntity? _modeSelect;
    private ClipboardText? _text;

    public ClipboardFeature()
        : base("clipboard", "Clipboard", "Stores and pastes text from Home Assistant.", PluginHostMode.Desktop)
    {
        Controls =
        [
            new InputTextControlDescriptor("ClipboardSlots", "Clipboard Slots",
                "Number of clipboard slots to expose.",
                defaultValue: "0", type: InputTextControlTypesEnum.Number),
            new SwitchControlDescriptor("ClipboardText", "Clipboard Text",
                "Notify entity that puts the text sent to it on the clipboard."),
        ];
    }

    public override IEntity[] GetEntities()
    {
        var entities = new List<IEntity>();

        if (_modeSelect != null)
            entities.Add(_modeSelect);

        foreach (var slot in _slots)
            entities.AddRange(slot.Entities);

        if (_text != null)
            entities.AddRange(_text.Entities);

        return [.. entities];
    }

    protected override void OnConfigured(IPluginConfig config)
    {
        var macConfig = (MacOsPluginConfig) config;
        var slots = macConfig.ClipboardSlots;

        _slots.Clear();

        for (var i = 1; i <= slots; i++)
            _slots.Add(new ClipboardSlot(i, _paster));

        _modeSelect = slots > 0 ? BuildModeSelect() : null;

        _text = macConfig.ClipboardText
            ? new ClipboardText(CreateLogger<ClipboardText>())
            : null;
    }

    protected override async Task OnEnableAsync()
    {
        if (_modeSelect != null)
            await _modeSelect.SetStateAsync(_paster.CurrentModeOption);

        if (_text != null)
            await _text.ModeSelect.SetStateAsync(_text.CurrentModeOption);
    }

    private SelectEntity BuildModeSelect()
    {
        return new SelectEntity
        {
            Name = "Clipboard Paste Mode",
            UniqueId = "macos_clipboard_mode",
            BaseTopic = "macos/clipboard/mode",
            Icon = "mdi:clipboard-flow",
            Options = [..ClipboardPaster.ModeOptions],
            DefaultState = _paster.CurrentModeOption,
            ChangeStateHandler = option =>
            {
                _paster.SetMode(option);
                return Task.FromResult(_paster.CurrentModeOption);
            }
        };
    }
}
