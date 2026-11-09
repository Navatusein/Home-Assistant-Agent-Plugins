using StrEnum;

namespace HomeAssistant.Agent.Plugin.Discord.Enums;

public class DiscordEventEnum: StringEnum<DiscordEventEnum>
{
    public static readonly DiscordEventEnum Ready = Define("READY");
    public static readonly DiscordEventEnum Error = Define("ERROR");

    public static readonly DiscordEventEnum GuildStatus = Define("GUILD_STATUS");
    public static readonly DiscordEventEnum GuildCreate = Define("GUILD_CREATE");

    public static readonly DiscordEventEnum ChannelCreate = Define("CHANNEL_CREATE");

    public static readonly DiscordEventEnum VoiceChannelSelect = Define("VOICE_CHANNEL_SELECT");

    public static readonly DiscordEventEnum VoiceStateCreate = Define("VOICE_STATE_CREATE");
    public static readonly DiscordEventEnum VoiceStateUpdate = Define("VOICE_STATE_UPDATE");
    public static readonly DiscordEventEnum VoiceStateDelete = Define("VOICE_STATE_DELETE");

    public static readonly DiscordEventEnum VoiceSettingsUpdate = Define("VOICE_SETTINGS_UPDATE");
    public static readonly DiscordEventEnum VoiceConnectionStatus = Define("VOICE_CONNECTION_STATUS");

    public static readonly DiscordEventEnum SpeakingStart = Define("SPEAKING_START");
    public static readonly DiscordEventEnum SpeakingStop = Define("SPEAKING_STOP");

    public static readonly DiscordEventEnum MessageCreate = Define("MESSAGE_CREATE");
    public static readonly DiscordEventEnum MessageUpdate = Define("MESSAGE_UPDATE");
    public static readonly DiscordEventEnum MessageDelete = Define("MESSAGE_DELETE");

    public static readonly DiscordEventEnum NotificationCreate = Define("NOTIFICATION_CREATE");

    public static readonly DiscordEventEnum ActivityJoin = Define("ACTIVITY_JOIN");
    public static readonly DiscordEventEnum ActivitySpectate = Define("ACTIVITY_SPECTATE");
    public static readonly DiscordEventEnum ActivityJoinRequest = Define("ACTIVITY_JOIN_REQUEST");
}