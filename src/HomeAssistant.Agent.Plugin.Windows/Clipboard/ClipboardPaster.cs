namespace HomeAssistant.Agent.Plugin.Windows.Clipboard;

internal enum ClipboardPasteMode
{
    // Type the text directly into the focused window (bypasses the clipboard).
    Type,
    // Put the text on the clipboard and paste it (text stays on the clipboard).
    Clipboard,
    // Back up the clipboard, paste the text, then restore the clipboard.
    ClipboardRestore
}

internal sealed class ClipboardPaster
{
    private const string TypeOption = "Type";
    private const string ClipboardOption = "Clipboard";
    private const string ClipboardRestoreOption = "Clipboard (restore)";

    // Time to let the target window read the clipboard before restoring it.
    private static readonly TimeSpan RestoreDelay = TimeSpan.FromMilliseconds(150);

    public static IReadOnlyList<string> ModeOptions => [TypeOption, ClipboardOption, ClipboardRestoreOption];

    public ClipboardPasteMode Mode { get; private set; } = ClipboardPasteMode.ClipboardRestore;

    public string CurrentModeOption => Mode switch
    {
        ClipboardPasteMode.Clipboard => ClipboardOption,
        ClipboardPasteMode.ClipboardRestore => ClipboardRestoreOption,
        _ => TypeOption
    };

    public void SetMode(string option)
    {
        Mode = option switch
        {
            ClipboardOption => ClipboardPasteMode.Clipboard,
            ClipboardRestoreOption => ClipboardPasteMode.ClipboardRestore,
            _ => ClipboardPasteMode.Type
        };
    }

    public async Task PasteAsync(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        switch (Mode)
        {
            case ClipboardPasteMode.Clipboard:
                ClipboardHelper.SetText(text);
                KeyboardInput.SendPaste();
                break;

            case ClipboardPasteMode.ClipboardRestore:
                var backup = ClipboardHelper.GetText();
                ClipboardHelper.SetText(text);
                KeyboardInput.SendPaste();
                await Task.Delay(RestoreDelay);
                ClipboardHelper.SetText(backup);
                break;

            default:
                KeyboardInput.TypeText(text);
                break;
        }
    }
}
