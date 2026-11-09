namespace HomeAssistant.Agent.Plugin.Windows.Keyboard;

// Resolves human friendly key names (as used in the plugin config) into
// Windows virtual-key codes. Names are case-insensitive. Letters A-Z, digits
// 0-9 and function keys F1-F24 are handled dynamically; everything else comes
// from the map below. The Extended flag marks keys that require
// KEYEVENTF_EXTENDEDKEY to be sent correctly (arrows, navigation, media, ...).
internal static class VirtualKeys
{
    private static readonly Dictionary<string, (ushort Vk, bool Extended)> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // Modifiers.
        ["ctrl"] = (0x11, false), ["control"] = (0x11, false),
        ["alt"] = (0x12, false), ["menu"] = (0x12, false),
        ["shift"] = (0x10, false),
        ["win"] = (0x5B, false), ["lwin"] = (0x5B, false), ["super"] = (0x5B, false),

        // Editing / navigation.
        ["enter"] = (0x0D, false), ["return"] = (0x0D, false),
        ["esc"] = (0x1B, false), ["escape"] = (0x1B, false),
        ["tab"] = (0x09, false),
        ["space"] = (0x20, false),
        ["backspace"] = (0x08, false), ["back"] = (0x08, false),
        ["capslock"] = (0x14, false),
        ["delete"] = (0x2E, true), ["del"] = (0x2E, true),
        ["insert"] = (0x2D, true), ["ins"] = (0x2D, true),
        ["home"] = (0x24, true), ["end"] = (0x23, true),
        ["pageup"] = (0x21, true), ["pgup"] = (0x21, true),
        ["pagedown"] = (0x22, true), ["pgdn"] = (0x22, true),
        ["up"] = (0x26, true), ["down"] = (0x28, true),
        ["left"] = (0x25, true), ["right"] = (0x27, true),
        ["printscreen"] = (0x2C, true), ["prtsc"] = (0x2C, true),
        ["apps"] = (0x5D, true), ["menukey"] = (0x5D, true),

        // Media / volume.
        ["volumeup"] = (0xAF, true), ["volumedown"] = (0xAE, true), ["volumemute"] = (0xAD, true),
        ["medianext"] = (0xB0, true), ["mediaprev"] = (0xB1, true),
        ["mediastop"] = (0xB2, true), ["mediaplaypause"] = (0xB3, true)
    };

    public static bool TryResolve(string name, out ushort vk, out bool extended)
    {
        vk = 0;
        extended = false;

        if (string.IsNullOrWhiteSpace(name))
            return false;

        var key = name.Trim();

        if (Map.TryGetValue(key, out var entry))
        {
            vk = entry.Vk;
            extended = entry.Extended;
            return true;
        }

        // Single letters and digits map directly onto their ASCII code.
        if (key.Length == 1)
        {
            var c = char.ToUpperInvariant(key[0]);

            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                vk = c;
                return true;
            }
        }

        // Function keys F1-F24 (VK_F1 = 0x70).
        if ((key[0] == 'F' || key[0] == 'f') && int.TryParse(key.AsSpan(1), out var fn) && fn is >= 1 and <= 24)
        {
            vk = (ushort) (0x70 + fn - 1);
            return true;
        }

        return false;
    }
}
