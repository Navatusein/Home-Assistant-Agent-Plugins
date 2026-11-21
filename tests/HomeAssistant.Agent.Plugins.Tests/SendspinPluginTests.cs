using HomeAssistant.Agent.Plugin.Sendspin;
using HomeAssistant.Agent.Plugin.Sendspin.Config;
using HomeAssistant.Agent.Plugin.Sendspin.Player;
using Sendspin.SDK.Client;

namespace HomeAssistant.Agent.Plugins.Tests;

public class SendspinPluginTests
{
    [Fact]
    public void Trims_the_player_name()
    {
        var config = (SendspinPluginConfig) new SendspinPlugin().CreateConfig(FormValues.Of(("PlayerName", "  Office  ")));

        Assert.Equal("Office", config.PlayerName);
        Assert.Equal("", ((SendspinPluginConfig) new SendspinPlugin().CreateConfig(FormValues.Of())).PlayerName);
    }

    // What Music Assistant set must come back after a restart: the last server
    // that played wins the next tie, and the output delay keeps the player in
    // sync with the rest of its group.
    [Fact]
    public void Store_survives_a_restart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sendspin-tests-" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            var store = new SendspinStore(directory);

            Assert.Null(((ILastPlayedServerStore) store).Load());
            Assert.Null(((IOutputDelayStore) store).Load());

            ((ILastPlayedServerStore) store).Save("server-1");
            ((IOutputDelayStore) store).Save(12.5);

            var reloaded = new SendspinStore(directory);

            Assert.Equal("server-1", ((ILastPlayedServerStore) reloaded).Load());
            Assert.Equal(12.5, ((IOutputDelayStore) reloaded).Load());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // A locked pairing is lifted by the next start only, and only once.
    [Fact]
    public void Pairing_gesture_is_taken_once()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sendspin-tests-" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            var store = new SendspinStore(directory);

            Assert.False(store.TakePairingGesture());

            store.RequestPairingGesture();

            Assert.True(new SendspinStore(directory).TakePairingGesture());
            Assert.False(new SendspinStore(directory).TakePairingGesture());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
