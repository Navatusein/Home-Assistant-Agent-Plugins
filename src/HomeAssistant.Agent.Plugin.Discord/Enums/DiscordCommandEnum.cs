using StrEnum;

namespace HomeAssistant.Agent.Plugin.Discord.Enums;

public class DiscordCommandEnum: StringEnum<DiscordCommandEnum>
{
    public static readonly DiscordCommandEnum Authenticate = Define("AUTHENTICATE");
    public static readonly DiscordCommandEnum Authorize = Define("AUTHORIZE");
    public static readonly DiscordCommandEnum GetVoiceSettings = Define("GET_VOICE_SETTINGS");
    public static readonly DiscordCommandEnum SetVoiceSettings = Define("SET_VOICE_SETTINGS");
    public static readonly DiscordCommandEnum Subscribe = Define("SUBSCRIBE");
    public static readonly DiscordCommandEnum UnSubscribe = Define("UNSUBSCRIBE");
    public static readonly DiscordCommandEnum Dispatch = Define("DISPATCH");
    public static readonly DiscordCommandEnum GetSelectedVoiceChannel = Define("GET_SELECTED_VOICE_CHANNEL");
    public static readonly DiscordCommandEnum SelectVoiceChannel = Define("SELECT_VOICE_CHANNEL");
}