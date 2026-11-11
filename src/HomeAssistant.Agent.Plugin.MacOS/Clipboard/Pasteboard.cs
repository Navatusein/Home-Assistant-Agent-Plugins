using HomeAssistant.Agent.Plugin.MacOS.Native;

namespace HomeAssistant.Agent.Plugin.MacOS.Clipboard;

// Text on the general pasteboard (NSPasteboard), the system clipboard.
internal static class Pasteboard
{
    private const string TextType = "public.utf8-plain-text";

    // The nspasteboard.org markers: clipboard managers and history tools skip
    // items that carry them (the counterpart of Windows' "exclude from
    // clipboard history").
    private static readonly string[] PrivateMarkers =
    [
        "org.nspasteboard.ConcealedType",
        "org.nspasteboard.TransientType"
    ];

    public static string GetText()
    {
        try
        {
            return ObjC.WithAutoreleasePool(() =>
            {
                using var type = new CFString(TextType);

                var text = ObjC.Send(General(), ObjC.Selector("stringForType:"), type.Handle);

                return CoreFoundation.GetString(text) ?? string.Empty;
            });
        }
        catch
        {
            return string.Empty;
        }
    }

    public static bool SetText(string text, bool hideFromHistory = false)
    {
        try
        {
            return ObjC.WithAutoreleasePool(() =>
            {
                var pasteboard = General();
                var setString = ObjC.Selector("setString:forType:");

                ObjC.SendLong(pasteboard, ObjC.Selector("clearContents"));

                if (string.IsNullOrEmpty(text))
                    return true;

                using var value = new CFString(text);
                using var type = new CFString(TextType);

                if (ObjC.SendBool(pasteboard, setString, value.Handle, type.Handle) == 0)
                    return false;

                if (hideFromHistory)
                {
                    using var empty = new CFString(string.Empty);

                    foreach (var marker in PrivateMarkers)
                    {
                        using var markerType = new CFString(marker);
                        ObjC.SendBool(pasteboard, setString, empty.Handle, markerType.Handle);
                    }
                }

                return true;
            });
        }
        catch
        {
            return false;
        }
    }

    private static IntPtr General() =>
        ObjC.Send(ObjC.Class("NSPasteboard"), ObjC.Selector("generalPasteboard"));
}
