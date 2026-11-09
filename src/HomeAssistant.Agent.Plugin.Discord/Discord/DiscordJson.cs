using System.Text.Encodings.Web;
using System.Text.Json;

namespace HomeAssistant.Agent.Plugin.Discord.Discord;

internal static class DiscordJson
{
    // Relaxed escaping keeps the frames (and the trace log) readable: the
    // default would write the nonce's '+' as \u002B.
    public static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
