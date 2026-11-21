using System.Text.Json;
using System.Text.Json.Serialization;
using HomeAssistant.Agent.Plugin.Base;

namespace HomeAssistant.Agent.Plugin.Ntfy.Ntfy;

// One line of ntfy's JSON stream (GET /<topics>/json): "open" once the
// subscription stands, "keepalive" every ~45 s, "message" for a message.
// Only what the plugin shows is read.
internal sealed record NtfyMessage
{
    public const string MessageEvent = "message";
    public const string OpenEvent = "open";

    // ntfy's default when a message names none.
    public const int DefaultPriority = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public string Id { get; init; } = string.Empty;

    public string Event { get; init; } = string.Empty;

    public string Topic { get; init; } = string.Empty;

    public string? Title { get; init; }

    public string? Message { get; init; }

    // 1 (min) … 5 (max); absent means 3.
    public int? Priority { get; init; }

    // Opened when the notification is clicked.
    public string? Click { get; init; }

    // The sender's logo (Home Assistant's ntfy.publish "icon", for one).
    public string? Icon { get; init; }

    public NtfyAttachment? Attachment { get; init; }

    public IReadOnlyList<NtfyAction>? Actions { get; init; }

    [JsonIgnore]
    public int EffectivePriority => Priority is >= 1 and <= 5 ? Priority.Value : DefaultPriority;

    // Null for a line that is not JSON: the stream goes on regardless.
    public static NtfyMessage? Parse(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<NtfyMessage>(line, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // What the user sees: ntfy titles a message without one by its topic, as
    // its apps do. High (4) and max (5) priority are the important ones. An
    // attached picture is shown; a "view" button opens its link; an "http"
    // one runs through runHttp, or is left out without it (null).
    public PluginNotification ToNotification(Func<NtfyAction, Func<Task>>? runHttp = null) =>
        new(string.IsNullOrWhiteSpace(Title) ? Topic : Title, Message ?? string.Empty)
        {
            Link = AbsoluteUri(Click),
            Important = EffectivePriority >= 4,
            Image = Attachment is { IsImage: true } attachment ? AbsoluteUri(attachment.Url) : null,
            Icon = AbsoluteUri(Icon),
            Actions = (Actions ?? [])
                .Select(action => action.ToButton(runHttp))
                .OfType<PluginNotificationAction>()
                .ToArray(),
        };

    internal static Uri? AbsoluteUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
}

internal sealed record NtfyAttachment
{
    public string? Name { get; init; }

    public string? Url { get; init; }

    // A MIME type, given when the file went through ntfy; an external one
    // (an "Attach" link) has none, only its name.
    public string? Type { get; init; }

    [JsonIgnore]
    public bool IsImage => Type != null
        ? Type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
        : Path.GetExtension(Name ?? Url ?? string.Empty).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp";
}

// A button: "view" opens a link, "http" sends a request (POST unless said
// otherwise), "broadcast" is for Android and not shown here.
internal sealed record NtfyAction
{
    public const string View = "view";
    public const string Http = "http";

    public string Action { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public string? Url { get; init; }

    public string? Method { get; init; }

    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    public string? Body { get; init; }

    public PluginNotificationAction? ToButton(Func<NtfyAction, Func<Task>>? runHttp)
    {
        if (string.IsNullOrWhiteSpace(Label) || NtfyMessage.AbsoluteUri(Url) is not { } url)
            return null;

        if (string.Equals(Action, View, StringComparison.OrdinalIgnoreCase))
            return new PluginNotificationAction(Label) { Link = url };

        if (string.Equals(Action, Http, StringComparison.OrdinalIgnoreCase) && runHttp != null)
            return new PluginNotificationAction(Label) { Clicked = runHttp(this) };

        return null;
    }
}
