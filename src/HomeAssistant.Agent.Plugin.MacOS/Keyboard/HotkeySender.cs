namespace HomeAssistant.Agent.Plugin.MacOS.Keyboard;

// Sends a key combination (e.g. Cmd+Option+Esc) to the focused app by
// resolving the configured key names and posting them.
public static class HotkeySender
{
    public static void Send(IReadOnlyList<string> keys)
    {
        if (keys.Count == 0)
            return;

        var resolved = new List<(ushort Code, ulong Modifier)>(keys.Count);

        foreach (var name in keys)
        {
            if (!KeyCodes.TryResolve(name, out var code, out var modifier))
                throw new ArgumentException($"Unknown key '{name}' in hotkey combination.", nameof(keys));

            resolved.Add((code, modifier));
        }

        KeyboardInput.SendKeyCombination(resolved);
    }
}
