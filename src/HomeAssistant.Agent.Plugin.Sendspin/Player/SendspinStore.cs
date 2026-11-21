using System.Globalization;
using Sendspin.SDK.Client;

namespace HomeAssistant.Agent.Plugin.Sendspin.Player;

// Where the player keeps what must survive a restart: its identity (the
// client_id Music Assistant knows it by is its public key), the pairing
// records, the pairing code lockout, the last server that played, the
// output delay Music Assistant set and a pending pairing gesture.
//
// Per user (%LOCALAPPDATA%, ~/Library/Application Support), not in the plugin
// storage: the identity and the pairing records are secrets, and the agent's
// data folder is open to every user of the computer. The SDK's file stores
// write atomically and keep the files to their owner.
internal sealed class SendspinStore : ILastPlayedServerStore, IOutputDelayStore
{
    public SendspinStore(string? directory = null)
    {
        Directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.Create),
            "HomeAssistantAgent", "Sendspin");

        System.IO.Directory.CreateDirectory(Directory);
    }

    public string Directory { get; }

    public string IdentityFile => Path.Combine(Directory, "identity.key");

    public string PairingFile => Path.Combine(Directory, "pairing.json");

    public string LockoutFile => Path.Combine(Directory, "pairing-lockout.json");

    private string LastPlayedFile => Path.Combine(Directory, "last-played.txt");

    private string OutputDelayFile => Path.Combine(Directory, "output-delay.txt");

    private string PairingGestureFile => Path.Combine(Directory, "pairing-gesture");

    // Pairing is locked and waits for the user: the next start allows it.
    public void RequestPairingGesture() => File.WriteAllText(PairingGestureFile, string.Empty);

    // Whether a locked pairing waits for this start; cleared either way.
    public bool TakePairingGesture()
    {
        if (!File.Exists(PairingGestureFile))
            return false;

        File.Delete(PairingGestureFile);
        return true;
    }

    string? ILastPlayedServerStore.Load() => Read(LastPlayedFile);

    void ILastPlayedServerStore.Save(string serverId) => File.WriteAllText(LastPlayedFile, serverId);

    double? IOutputDelayStore.Load() =>
        double.TryParse(Read(OutputDelayFile), NumberStyles.Float, CultureInfo.InvariantCulture, out var delay)
            ? delay
            : null;

    void IOutputDelayStore.Save(double outputDelayMs) =>
        File.WriteAllText(OutputDelayFile, outputDelayMs.ToString(CultureInfo.InvariantCulture));

    private static string? Read(string path) => File.Exists(path) ? File.ReadAllText(path).Trim() : null;
}
