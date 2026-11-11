using System.Runtime.InteropServices;

namespace HomeAssistant.Agent.Plugin.MacOS.Native;

internal static unsafe class CoreGraphics
{
    private const string Lib = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    public const int EventSourceStateHidSystem = 1;
    public const uint HidEventTap = 0;

    public const ulong FlagShift = 0x00020000;
    public const ulong FlagControl = 0x00040000;
    public const ulong FlagOption = 0x00080000;
    public const ulong FlagCommand = 0x00100000;
    public const ulong FlagFunction = 0x00800000;

    [DllImport(Lib)]
    public static extern IntPtr CGEventSourceCreate(int stateId);

    [DllImport(Lib)]
    public static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort keyCode,
        [MarshalAs(UnmanagedType.I1)] bool keyDown);

    [DllImport(Lib)]
    public static extern void CGEventSetFlags(IntPtr evt, ulong flags);

    [DllImport(Lib)]
    public static extern void CGEventKeyboardSetUnicodeString(IntPtr evt, nuint length, char* text);

    [DllImport(Lib)]
    public static extern void CGEventPost(uint tap, IntPtr evt);
}
