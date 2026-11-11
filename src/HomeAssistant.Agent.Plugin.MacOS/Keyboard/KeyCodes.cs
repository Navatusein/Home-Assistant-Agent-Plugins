using HomeAssistant.Agent.Plugin.MacOS.Native;

namespace HomeAssistant.Agent.Plugin.MacOS.Keyboard;

// Key names used in hotkey combinations, mapped to macOS virtual key codes
// (kVK_* in Carbon's Events.h — positions on the ANSI layout). Modifier keys
// also carry their event flag. Case-insensitive.
public static class KeyCodes
{
    private static readonly Dictionary<string, (ushort Code, ulong Modifier)> Map =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Modifiers. Ctrl and Win are accepted, so a combination written
            // for Windows still means something.
            ["cmd"] = (55, CoreGraphics.FlagCommand),
            ["command"] = (55, CoreGraphics.FlagCommand),
            ["win"] = (55, CoreGraphics.FlagCommand),
            ["meta"] = (55, CoreGraphics.FlagCommand),
            ["shift"] = (56, CoreGraphics.FlagShift),
            ["option"] = (58, CoreGraphics.FlagOption),
            ["opt"] = (58, CoreGraphics.FlagOption),
            ["alt"] = (58, CoreGraphics.FlagOption),
            ["ctrl"] = (59, CoreGraphics.FlagControl),
            ["control"] = (59, CoreGraphics.FlagControl),
            ["fn"] = (63, CoreGraphics.FlagFunction),

            // Letters.
            ["a"] = (0, 0), ["s"] = (1, 0), ["d"] = (2, 0), ["f"] = (3, 0), ["h"] = (4, 0),
            ["g"] = (5, 0), ["z"] = (6, 0), ["x"] = (7, 0), ["c"] = (8, 0), ["v"] = (9, 0),
            ["b"] = (11, 0), ["q"] = (12, 0), ["w"] = (13, 0), ["e"] = (14, 0), ["r"] = (15, 0),
            ["y"] = (16, 0), ["t"] = (17, 0), ["o"] = (31, 0), ["u"] = (32, 0), ["i"] = (34, 0),
            ["p"] = (35, 0), ["l"] = (37, 0), ["j"] = (38, 0), ["k"] = (40, 0), ["n"] = (45, 0),
            ["m"] = (46, 0),

            // Digits of the main row.
            ["1"] = (18, 0), ["2"] = (19, 0), ["3"] = (20, 0), ["4"] = (21, 0), ["5"] = (23, 0),
            ["6"] = (22, 0), ["7"] = (26, 0), ["8"] = (28, 0), ["9"] = (25, 0), ["0"] = (29, 0),

            // Punctuation.
            ["minus"] = (27, 0), ["-"] = (27, 0),
            ["equal"] = (24, 0), ["="] = (24, 0),
            ["leftbracket"] = (33, 0), ["["] = (33, 0),
            ["rightbracket"] = (30, 0), ["]"] = (30, 0),
            ["semicolon"] = (41, 0), [";"] = (41, 0),
            ["quote"] = (39, 0), ["'"] = (39, 0),
            ["backslash"] = (42, 0), ["\\"] = (42, 0),
            ["comma"] = (43, 0), [","] = (43, 0),
            ["period"] = (47, 0), ["dot"] = (47, 0), ["."] = (47, 0),
            ["slash"] = (44, 0), ["/"] = (44, 0),
            ["grave"] = (50, 0), ["backtick"] = (50, 0), ["`"] = (50, 0),

            // Editing and navigation.
            ["return"] = (36, 0), ["enter"] = (36, 0),
            ["tab"] = (48, 0),
            ["space"] = (49, 0),
            ["backspace"] = (51, 0), ["delete"] = (51, 0),
            ["forwarddelete"] = (117, 0), ["del"] = (117, 0),
            ["esc"] = (53, 0), ["escape"] = (53, 0),
            ["capslock"] = (57, 0),
            ["home"] = (115, 0), ["end"] = (119, 0),
            ["pageup"] = (116, 0), ["pgup"] = (116, 0),
            ["pagedown"] = (121, 0), ["pgdn"] = (121, 0),
            ["left"] = (123, 0), ["right"] = (124, 0), ["down"] = (125, 0), ["up"] = (126, 0),
            ["help"] = (114, 0),

            // Function keys.
            ["f1"] = (122, 0), ["f2"] = (120, 0), ["f3"] = (99, 0), ["f4"] = (118, 0),
            ["f5"] = (96, 0), ["f6"] = (97, 0), ["f7"] = (98, 0), ["f8"] = (100, 0),
            ["f9"] = (101, 0), ["f10"] = (109, 0), ["f11"] = (103, 0), ["f12"] = (111, 0),
            ["f13"] = (105, 0), ["f14"] = (107, 0), ["f15"] = (113, 0), ["f16"] = (106, 0),
            ["f17"] = (64, 0), ["f18"] = (79, 0), ["f19"] = (80, 0), ["f20"] = (90, 0),
        };

    public static bool TryResolve(string name, out ushort code, out ulong modifier)
    {
        if (Map.TryGetValue(name.Trim(), out var key))
        {
            code = key.Code;
            modifier = key.Modifier;
            return true;
        }

        code = 0;
        modifier = 0;
        return false;
    }
}
