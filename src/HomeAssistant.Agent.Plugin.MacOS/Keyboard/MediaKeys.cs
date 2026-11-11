using System.Runtime.InteropServices;
using HomeAssistant.Agent.Plugin.MacOS.Native;

namespace HomeAssistant.Agent.Plugin.MacOS.Keyboard;

// The play/pause, next and previous keys of the Mac keyboard. They are not
// key codes but "system defined" events (subtype 8, NX_KEYTYPE_* in data1),
// which only NSEvent can build; the Quartz event behind it is then posted
// like any keystroke. Whatever app owns Now Playing reacts, as with the keys.
internal static class MediaKeys
{
    public const int PlayPause = 16;
    public const int Next = 17;
    public const int Previous = 18;

    private const ulong EventTypeSystemDefined = 14;
    private const short SubtypeAuxControlButtons = 8;

    [StructLayout(LayoutKind.Sequential)]
    private struct NSPoint
    {
        public double X;
        public double Y;
    }

    // +[NSEvent otherEventWithType:location:modifierFlags:timestamp:windowNumber:context:subtype:data1:data2:]
    [DllImport(ObjC.Lib, EntryPoint = "objc_msgSend")]
    private static extern IntPtr OtherEvent(IntPtr cls, IntPtr selector, ulong type, NSPoint location,
        ulong modifierFlags, double timestamp, long windowNumber, IntPtr context, short subtype, long data1,
        long data2);

    public static void Press(int key)
    {
        Accessibility.Require();

        Post(key, down: true);
        Post(key, down: false);
    }

    private static void Post(int key, bool down)
    {
        ObjC.WithAutoreleasePool(() =>
        {
            var state = down ? 0xA : 0xB;

            var evt = OtherEvent(
                ObjC.Class("NSEvent"),
                ObjC.Selector("otherEventWithType:location:modifierFlags:timestamp:windowNumber:context:subtype:data1:data2:"),
                EventTypeSystemDefined,
                default,
                (ulong) state << 8,
                0,
                0,
                IntPtr.Zero,
                SubtypeAuxControlButtons,
                (long) key << 16 | (long) state << 8,
                -1);

            if (evt == IntPtr.Zero)
                throw new InvalidOperationException("NSEvent did not create the media key event.");

            // Owned by the NSEvent: not released here.
            var cgEvent = ObjC.Send(evt, ObjC.Selector("CGEvent"));

            CoreGraphics.CGEventPost(CoreGraphics.HidEventTap, cgEvent);
            return true;
        });
    }
}
