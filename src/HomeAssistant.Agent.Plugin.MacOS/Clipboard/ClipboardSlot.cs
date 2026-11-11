using HomeAssistant.Connector.Entities;

namespace HomeAssistant.Agent.Plugin.MacOS.Clipboard;

internal sealed class ClipboardSlot
{
    private const string EmptyLabel = "Empty";
    private const int PreviewMaxLength = 255;

    private string? _content;

    private readonly ClipboardPaster _paster;

    private readonly SensorEntity _sensor;
    private readonly ButtonEntity _saveOrPasteButton;
    private readonly ButtonEntity _clearButton;

    public ClipboardSlot(int index, ClipboardPaster paster)
    {
        _paster = paster;

        _sensor = new SensorEntity
        {
            Name = $"Clipboard Slot {index}",
            UniqueId = $"macos_clipboard_slot_{index}",
            BaseTopic = $"macos/clipboard/slot-{index}",
            Icon = "mdi:clipboard-text",
            DefaultState = EmptyLabel
        };

        _saveOrPasteButton = new ButtonEntity
        {
            Name = $"Clipboard Slot {index} Save/Paste",
            UniqueId = $"macos_clipboard_slot_{index}_button",
            BaseTopic = $"macos/clipboard/slot-{index}-button",
            Icon = "mdi:clipboard-arrow-down",
            ClickHandler = SaveOrPasteAsync
        };

        _clearButton = new ButtonEntity
        {
            Name = $"Clipboard Slot {index} Clear",
            UniqueId = $"macos_clipboard_slot_{index}_clear",
            BaseTopic = $"macos/clipboard/slot-{index}-clear",
            Icon = "mdi:delete",
            ClickHandler = ClearAsync
        };
    }

    public IEnumerable<IEntity> Entities => [_sensor, _saveOrPasteButton, _clearButton];

    private async Task SaveOrPasteAsync()
    {
        if (string.IsNullOrEmpty(_content))
        {
            _content = Pasteboard.GetText();
            await _sensor.SetStateAsync(BuildPreview(_content));
        }
        else
        {
            await _paster.PasteAsync(_content);
        }
    }

    private async Task ClearAsync()
    {
        _content = null;
        await _sensor.SetStateAsync(EmptyLabel);
    }

    private static string BuildPreview(string text)
    {
        if (string.IsNullOrEmpty(text))
            return EmptyLabel;

        var singleLine = text.ReplaceLineEndings(" ");

        return singleLine.Length <= PreviewMaxLength
            ? singleLine
            : string.Concat(singleLine.AsSpan(0, PreviewMaxLength - 1), "…");
    }
}
