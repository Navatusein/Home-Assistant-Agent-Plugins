using System.Runtime.InteropServices;

namespace HomeAssistant.Agent.Plugin.MacOS.Native;

// Posting keystrokes (hotkeys, media keys, paste, typing) needs the app to be
// allowed under Accessibility in System Settings; without it macOS drops
// the events silently. The first refusal shows the system prompt.
internal static class Accessibility
{
    private const string Lib = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    private static int _prompted;

    [DllImport(Lib)]
    private static extern byte AXIsProcessTrusted();

    [DllImport(Lib)]
    private static extern byte AXIsProcessTrustedWithOptions(IntPtr options);

    public static void Require()
    {
        if (AXIsProcessTrusted() != 0)
            return;

        if (Interlocked.Exchange(ref _prompted, 1) == 0)
            Prompt();

        throw new InvalidOperationException(
            "Home Assistant Agent may not control the computer: allow it under Accessibility in System Settings.");
    }

    private static void Prompt()
    {
        try
        {
            var library = NativeLibrary.Load(Lib);
            var promptKey = Marshal.ReadIntPtr(NativeLibrary.GetExport(library, "kAXTrustedCheckOptionPrompt"));
            var options = CoreFoundation.CreateDictionary(promptKey, CoreFoundation.True);

            try
            {
                AXIsProcessTrustedWithOptions(options);
            }
            finally
            {
                CoreFoundation.CFRelease(options);
            }
        }
        catch
        {
            // Only the prompt failed; Require still reports the refusal.
        }
    }
}
