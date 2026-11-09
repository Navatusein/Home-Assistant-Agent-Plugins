using HomeAssistant.Agent.Plugin.Windows.Clipboard;

namespace HomeAssistant.Agent.Plugin.Windows.Keyboard;

// Sends a key combination (e.g. Ctrl+Shift+Esc) to the currently focused
// window by resolving the configured key names and forwarding them to the
// low-level input primitive.
public static class HotkeySender
{
    public static void Send(IReadOnlyList<string> keys)
    {
        if (keys.Count == 0)
            return;

        var resolved = new List<(ushort Vk, bool Extended)>(keys.Count);

        foreach (var name in keys)
        {
            if (!VirtualKeys.TryResolve(name, out var vk, out var extended))
                throw new ArgumentException($"Unknown key '{name}' in hotkey combination.", nameof(keys));

            resolved.Add((vk, extended));
        }

        KeyboardInput.SendKeyCombination(resolved);
    }
}
