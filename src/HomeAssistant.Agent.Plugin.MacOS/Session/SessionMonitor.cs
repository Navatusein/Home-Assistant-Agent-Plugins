using System.Xml;
using System.Xml.Linq;
using HomeAssistant.Agent.Plugin.MacOS.Common;
using Microsoft.Extensions.Logging;

namespace HomeAssistant.Agent.Plugin.MacOS.Session;

// Polls whether a user's desktop is on screen and unlocked. The service is a
// LaunchDaemon, outside every user session, so it gets no lock notifications;
// the window server publishes the console sessions in the I/O Registry
// (IOConsoleUsers on the root entry) instead, and ioreg reads them as root
// as well as before anyone has logged in. Changed fires only when the value
// actually flips.
public sealed class SessionMonitor : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan IoregTimeout = TimeSpan.FromSeconds(10);

    private readonly ILogger _logger;

    private Poller? _poller;

    public SessionMonitor(ILogger logger)
    {
        _logger = logger;
    }

    public bool IsSessionUnlocked { get; private set; }

    public event Action? Changed;

    public void Start()
    {
        // First poll runs synchronously so the property holds a real value
        // before the caller publishes the initial state.
        Poll(raiseChanged: false);
        _poller = new Poller(PollInterval, () => Poll(raiseChanged: true), _logger);
    }

    public void Dispose()
    {
        _poller?.Dispose();
        _poller = null;
    }

    private void Poll(bool raiseChanged)
    {
        bool unlocked;

        try
        {
            unlocked = QuerySessionUnlocked();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the console sessions");
            return;
        }

        if (unlocked == IsSessionUnlocked)
            return;

        IsSessionUnlocked = unlocked;

        if (raiseChanged)
            Changed?.Invoke();
    }

    private static bool QuerySessionUnlocked()
    {
        var result = Command.Run("/usr/sbin/ioreg", ["-n", "Root", "-d1", "-a"], IoregTimeout);

        if (result.ExitCode != 0)
            throw new InvalidOperationException($"ioreg exited with {result.ExitCode}: {result.Error.Trim()}");

        return ParseSessionUnlocked(result.Output);
    }

    // ioreg -a prints a property list. A session counts when its user is on
    // the console and has finished logging in, and the screen is not locked.
    internal static bool ParseSessionUnlocked(string plist)
    {
        using var reader = XmlReader.Create(new StringReader(plist),
            new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });

        var users = XDocument.Load(reader)
            .Descendants("key")
            .FirstOrDefault(key => key.Value == "IOConsoleUsers")
            ?.ElementsAfterSelf()
            .FirstOrDefault();

        if (users?.Name != "array")
            return false;

        foreach (var session in users.Elements("dict").Select(ReadDict))
        {
            var onConsole = Flag(session, "kCGSSessionOnConsoleKey") ?? false;
            var loginDone = Flag(session, "kCGSessionLoginDoneKey") ?? Flag(session, "kCGSSessionLoginDoneKey") ?? true;
            var locked = Flag(session, "CGSSessionScreenIsLocked") ?? false;

            if (onConsole && loginDone && !locked)
                return true;
        }

        return false;
    }

    private static Dictionary<string, XElement> ReadDict(XElement dict)
    {
        var values = new Dictionary<string, XElement>();

        foreach (var key in dict.Elements("key"))
        {
            if (key.ElementsAfterSelf().FirstOrDefault() is { } value)
                values[key.Value] = value;
        }

        return values;
    }

    private static bool? Flag(Dictionary<string, XElement> dict, string key) =>
        dict.TryGetValue(key, out var value) && value.Name.LocalName is "true" or "false"
            ? value.Name.LocalName == "true"
            : null;
}
