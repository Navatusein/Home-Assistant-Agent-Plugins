using HomeAssistant.Connector.Entities;
using Microsoft.Extensions.Logging;

namespace HomeAssistant.Agent.Plugin.Windows.Clipboard;

// Text sent from Home Assistant (notify.send_message): pasted into the focused
// window the way the mode select says (by default through the clipboard, which
// is restored afterwards, like the slots), or only put on the clipboard (Copy).
// Off unless switched on: anyone who may publish to the topic can type into
// whatever window has focus.
internal sealed class ClipboardText
{
    private const int MaxLength = 64 * 1024;
    private const string CopyOption = "Copy";

    private readonly ILogger _logger;
    private readonly ClipboardPaster _paster = new();

    private bool _copyOnly;

    public ClipboardText(ILogger logger)
    {
        _logger = logger;

        Notify = new NotifyEntity
        {
            Name = "Clipboard Text",
            UniqueId = "windows_clipboard_text",
            BaseTopic = "windows/clipboard/text",
            Icon = "mdi:clipboard-text-outline",
            MessageHandler = HandleMessageAsync
        };

        ModeSelect = new SelectEntity
        {
            Name = "Clipboard Text Mode",
            UniqueId = "windows_clipboard_text_mode",
            BaseTopic = "windows/clipboard/text-mode",
            Icon = "mdi:clipboard-flow",
            Options = [CopyOption, ..ClipboardPaster.ModeOptions],
            DefaultState = _paster.CurrentModeOption,
            ChangeStateHandler = option =>
            {
                SetMode(option);
                return Task.FromResult(CurrentModeOption);
            }
        };
    }

    public NotifyEntity Notify { get; }

    public SelectEntity ModeSelect { get; }

    public IEnumerable<IEntity> Entities => [Notify, ModeSelect];

    public string CurrentModeOption => _copyOnly ? CopyOption : _paster.CurrentModeOption;

    private void SetMode(string option)
    {
        _copyOnly = option == CopyOption;

        if (!_copyOnly)
            _paster.SetMode(option);

        _logger.LogInformation("Clipboard text mode: {Mode}", CurrentModeOption);
    }

    private async Task HandleMessageAsync(string message)
    {
        if (message.Length > MaxLength)
        {
            _logger.LogWarning("Clipboard text of {Length} chars rejected: the limit is {MaxLength}", message.Length, MaxLength);
            return;
        }

        if (!_copyOnly)
        {
            await _paster.PasteAsync(message);
            _logger.LogInformation("Text from Home Assistant pasted ({Mode}, {Length} chars)", CurrentModeOption, message.Length);
            return;
        }

        if (ClipboardHelper.SetPrivateText(message))
            _logger.LogInformation("Clipboard set from Home Assistant ({Length} chars)", message.Length);
        else
            _logger.LogWarning("Clipboard is locked by another process — text from Home Assistant dropped");
    }
}
