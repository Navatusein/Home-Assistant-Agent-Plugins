using System.Text.Json.Serialization;

namespace HomeAssistant.Agent.Plugin.Discord.Contracts.Responses;

public class VoiceSettingsResponse
{
    [JsonPropertyName("input")]
    public object Input { get; set; } = null!;

    [JsonPropertyName("output")]
    public object Output { get; set; } = null!;

    [JsonPropertyName("mode")]
    public object Mode { get; set; } = null!;

    [JsonPropertyName("automatic_gain_control")]
    public bool IsAutomaticGainControlEnabled { get; set; }

    [JsonPropertyName("echo_cancellation")]
    public bool IsEchoCancellationEnabled { get; set; }

    [JsonPropertyName("noise_suppression")]
    public bool IsNoiseSuppressionEnabled { get; set; }

    [JsonPropertyName("qos")]
    public bool IsQosEnabled { get; set; }

    [JsonPropertyName("silence_warning")]
    public bool IsSilenceWarningShown { get; set; }

    [JsonPropertyName("deaf")]
    public bool IsDeaf { get; set; }

    [JsonPropertyName("mute")]
    public bool IsMute { get; set; }
}