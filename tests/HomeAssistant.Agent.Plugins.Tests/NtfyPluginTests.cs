using HomeAssistant.Agent.Plugin.Ntfy;
using HomeAssistant.Agent.Plugin.Ntfy.Config;
using HomeAssistant.Agent.Plugin.Ntfy.Ntfy;

namespace HomeAssistant.Agent.Plugins.Tests;

public class NtfyPluginTests
{
    [Fact]
    public void Reads_the_config()
    {
        var config = (NtfyPluginConfig) new NtfyPlugin().CreateConfig(FormValues.Of(
            ("Server", " ntfy.example.com/ "),
            ("Topics", new object[]
            {
                new { Topic = " alerts " },
                new { Topic = "backups" },
                new { Topic = "" },
                new { Topic = "alerts" },
                new { Topic = "bad/topic" },
            }),
            ("Token", " tk_abc "),
            ("MinPriority", "4")));

        Assert.Equal("https://ntfy.example.com", config.Server);
        Assert.Equal(["alerts", "backups"], config.Topics);
        Assert.Equal("tk_abc", config.Token);
        Assert.Equal(4, config.MinPriority);
    }

    [Fact]
    public void Defaults_to_ntfy_sh_and_every_priority()
    {
        var config = (NtfyPluginConfig) new NtfyPlugin().CreateConfig(FormValues.Of());

        Assert.Equal("https://ntfy.sh", config.Server);
        Assert.Empty(config.Topics);
        Assert.Equal(1, config.MinPriority);
    }

    [Theory]
    [InlineData("http://192.168.1.5:8080", "http://192.168.1.5:8080")]
    [InlineData("https://ntfy.example.com/ntfy/", "https://ntfy.example.com/ntfy")]
    [InlineData("  ", "https://ntfy.sh")]
    public void Normalizes_the_server(string server, string expected) =>
        Assert.Equal(expected, NtfyPlugin.NormalizeServer(server));

    // The shape ntfy writes, one JSON object per line.
    [Fact]
    public void Turns_a_message_into_a_notification()
    {
        var message = NtfyMessage.Parse("""
            {"id":"sPs71M8A2T","time":1700000000,"expires":1700043200,"event":"message","topic":"backups",
             "title":"Backup failed","message":"Disk full on nas","priority":5,"tags":["warning"],
             "click":"https://nas.local/backups","actions":[{"action":"view","label":"Open","url":"https://x"}]}
            """)!;

        Assert.Equal(NtfyMessage.MessageEvent, message.Event);

        var notification = message.ToNotification();

        Assert.Equal("Backup failed", notification.Title);
        Assert.Equal("Disk full on nas", notification.Message);
        Assert.Equal(new Uri("https://nas.local/backups"), notification.Link);
        Assert.True(notification.Important);
    }

    // As ntfy's apps do: no title shows the topic; no priority is 3, not important.
    [Fact]
    public void A_plain_message_is_titled_by_its_topic()
    {
        var message = NtfyMessage.Parse("""{"id":"a","event":"message","topic":"alerts","message":"Door open"}""")!;
        var notification = message.ToNotification();

        Assert.Equal(NtfyMessage.DefaultPriority, message.EffectivePriority);
        Assert.Equal("alerts", notification.Title);
        Assert.Null(notification.Link);
        Assert.False(notification.Important);
    }

    [Fact]
    public void Keepalives_and_garbage_are_no_messages()
    {
        Assert.Equal("keepalive", NtfyMessage.Parse("""{"id":"k","time":1,"event":"keepalive","topic":"alerts"}""")?.Event);
        Assert.Null(NtfyMessage.Parse("not json"));
    }

    // What ntfy writes for an attached picture and buttons.
    private const string RichMessage = """
        {"id":"r","event":"message","topic":"doors","title":"Front door","message":"Someone rang",
         "icon":"https://cdn.example.com/doorbell.png",
         "attachment":{"name":"snapshot.jpg","type":"image/jpeg","size":48213,"url":"https://ntfy.sh/file/abc.jpg"},
         "actions":[
           {"action":"view","label":"Camera","url":"https://ha.local/camera"},
           {"action":"http","label":"Open door","url":"https://ha.local/api/open","method":"PUT","headers":{"Authorization":"Bearer x"},"body":"{}"},
           {"action":"broadcast","label":"Tasker","extras":{"cmd":"pic"}}
         ]}
        """;

    [Fact]
    public void Shows_an_attached_picture_and_view_buttons()
    {
        var notification = NtfyMessage.Parse(RichMessage)!.ToNotification();

        Assert.Equal(new Uri("https://ntfy.sh/file/abc.jpg"), notification.Image);
        Assert.Equal(new Uri("https://cdn.example.com/doorbell.png"), notification.Icon);

        // Without HTTP buttons allowed, only the link one.
        var button = Assert.Single(notification.Actions);
        Assert.Equal(("Camera", new Uri("https://ha.local/camera")), (button.Label, button.Link));
    }

    [Fact]
    public void Http_buttons_run_through_the_plugin_once_allowed()
    {
        NtfyAction? ran = null;
        var notification = NtfyMessage.Parse(RichMessage)!.ToNotification(action => () =>
        {
            ran = action;
            return Task.CompletedTask;
        });

        Assert.Equal(["Camera", "Open door"], notification.Actions.Select(action => action.Label));

        notification.Actions[1].Clicked!();

        Assert.Equal(("PUT", "https://ha.local/api/open", "Bearer x", "{}"),
            (ran!.Method, ran.Url, ran.Headers!["Authorization"], ran.Body));
    }

    // A file that is not a picture is not shown as one; an external link
    // without a type counts by its name.
    [Theory]
    [InlineData("""{"name":"report.pdf","type":"application/pdf","url":"https://x/report.pdf"}""", false)]
    [InlineData("""{"name":"cat.PNG","url":"https://x/cat.PNG"}""", true)]
    public void Recognizes_pictures(string attachment, bool picture)
    {
        var message = NtfyMessage.Parse($$"""{"event":"message","topic":"t","attachment":{{attachment}}}""")!;

        Assert.Equal(picture, message.ToNotification().Image != null);
    }
}
