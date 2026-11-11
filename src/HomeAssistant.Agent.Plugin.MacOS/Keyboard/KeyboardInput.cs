using HomeAssistant.Agent.Plugin.MacOS.Native;

namespace HomeAssistant.Agent.Plugin.MacOS.Keyboard;

// Keystrokes posted to the focused app through Quartz events.
internal static unsafe class KeyboardInput
{
    private const ushort KeyReturn = 36;
    private const ushort KeyCommand = 55;
    private const ushort KeyV = 9;

    // A keyboard event carries at most 20 UTF-16 units of text.
    private const int TextChunk = 20;

    // Presses the keys down in order, then releases them in reverse order, so
    // modifiers wrap the final key (Cmd down, Option down, Esc down, Esc up,
    // Option up, Cmd up). Each event carries the modifiers held at that point.
    public static void SendKeyCombination(IReadOnlyList<(ushort Code, ulong Modifier)> keys)
    {
        if (keys.Count == 0)
            return;

        Accessibility.Require();

        var source = CoreGraphics.CGEventSourceCreate(CoreGraphics.EventSourceStateHidSystem);

        try
        {
            ulong flags = 0;

            foreach (var (code, modifier) in keys)
            {
                flags |= modifier;
                PostKey(source, code, true, flags);
            }

            for (var i = keys.Count - 1; i >= 0; i--)
            {
                var (code, modifier) = keys[i];
                flags &= ~modifier;
                PostKey(source, code, false, flags);
            }
        }
        finally
        {
            Release(source);
        }
    }

    // Sends Cmd+V to the focused app.
    public static void SendPaste()
    {
        SendKeyCombination([(KeyCommand, CoreGraphics.FlagCommand), (KeyV, 0)]);
    }

    // Types the text into the focused app as key strokes, without touching
    // the clipboard.
    public static void TypeText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        Accessibility.Require();

        var source = CoreGraphics.CGEventSourceCreate(CoreGraphics.EventSourceStateHidSystem);

        try
        {
            var start = 0;

            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];

                if (ch is '\r' or '\n')
                {
                    PostText(source, text.AsSpan(start, i - start));

                    if (ch == '\n')
                    {
                        PostKey(source, KeyReturn, true, 0);
                        PostKey(source, KeyReturn, false, 0);
                    }

                    start = i + 1;
                }
                else if (i - start == TextChunk)
                {
                    // The chunk is full; a surrogate pair stays together.
                    var cut = char.IsLowSurrogate(ch) ? i - 1 : i;

                    PostText(source, text.AsSpan(start, cut - start));
                    start = cut;
                }
            }

            PostText(source, text.AsSpan(start));
        }
        finally
        {
            Release(source);
        }
    }

    private static void PostKey(IntPtr source, ushort code, bool down, ulong flags)
    {
        var evt = CoreGraphics.CGEventCreateKeyboardEvent(source, code, down);

        try
        {
            CoreGraphics.CGEventSetFlags(evt, flags);
            CoreGraphics.CGEventPost(CoreGraphics.HidEventTap, evt);
        }
        finally
        {
            Release(evt);
        }
    }

    private static void PostText(IntPtr source, ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
            return;

        foreach (var down in (bool[]) [true, false])
        {
            var evt = CoreGraphics.CGEventCreateKeyboardEvent(source, 0, down);

            try
            {
                fixed (char* chars = text)
                    CoreGraphics.CGEventKeyboardSetUnicodeString(evt, (nuint) text.Length, chars);

                CoreGraphics.CGEventSetFlags(evt, 0);
                CoreGraphics.CGEventPost(CoreGraphics.HidEventTap, evt);
            }
            finally
            {
                Release(evt);
            }
        }

        // Some apps drop key events that arrive too close together.
        Thread.Sleep(2);
    }

    private static void Release(IntPtr cf)
    {
        if (cf != IntPtr.Zero)
            CoreFoundation.CFRelease(cf);
    }
}
