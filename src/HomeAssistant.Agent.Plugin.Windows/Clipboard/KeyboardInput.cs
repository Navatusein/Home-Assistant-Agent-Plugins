using System.Runtime.InteropServices;

namespace HomeAssistant.Agent.Plugin.Windows.Clipboard;

internal static class KeyboardInput
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventExtended = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;
    private const ushort VkReturn = 0x0D;
    private const ushort VkControl = 0x11;
    private const ushort VkV = 0x56;

    // Types the text directly into the focused window as key strokes, without
    // touching the clipboard.
    public static void TypeText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var inputs = new List<INPUT>(text.Length * 2);

        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\r':
                    continue;
                case '\n':
                    inputs.Add(MakeInput(VkReturn, 0, 0));
                    inputs.Add(MakeInput(VkReturn, 0, KeyEventKeyUp));
                    break;
                default:
                    inputs.Add(MakeInput(0, ch, KeyEventUnicode));
                    inputs.Add(MakeInput(0, ch, KeyEventUnicode | KeyEventKeyUp));
                    break;
            }
        }

        if (inputs.Count == 0)
            return;

        var array = inputs.ToArray();
        SendInput((uint) array.Length, array, Marshal.SizeOf<INPUT>());
    }

    // Sends the Ctrl+V shortcut to the focused window.
    public static void SendPaste()
    {
        INPUT[] inputs =
        [
            MakeInput(VkControl, 0, 0),
            MakeInput(VkV, 0, 0),
            MakeInput(VkV, 0, KeyEventKeyUp),
            MakeInput(VkControl, 0, KeyEventKeyUp)
        ];

        SendInput((uint) inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    // Presses the given keys down in order, then releases them in reverse
    // order, so modifiers wrap the final key (e.g. Ctrl down, Shift down,
    // Esc down, Esc up, Shift up, Ctrl up).
    public static void SendKeyCombination(IReadOnlyList<(ushort Vk, bool Extended)> keys)
    {
        if (keys.Count == 0)
            return;

        var inputs = new INPUT[keys.Count * 2];

        for (var i = 0; i < keys.Count; i++)
        {
            var (vk, extended) = keys[i];
            inputs[i] = MakeInput(vk, 0, extended ? KeyEventExtended : 0);
        }

        for (var i = 0; i < keys.Count; i++)
        {
            var (vk, extended) = keys[keys.Count - 1 - i];
            inputs[keys.Count + i] = MakeInput(vk, 0, KeyEventKeyUp | (extended ? KeyEventExtended : 0));
        }

        SendInput((uint) inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT MakeInput(ushort virtualKey, ushort scanCode, uint flags)
    {
        return new INPUT
        {
            type = InputKeyboard,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKey,
                    wScan = scanCode,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }
}
