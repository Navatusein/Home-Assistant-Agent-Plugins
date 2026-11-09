using System.Runtime.InteropServices;
using Timer = System.Threading.Timer;

namespace HomeAssistant.Agent.Plugin.Windows.Session;

// Polls whether the console session shows the interactive desktop — an
// authenticated user with the screen not locked. The service (session 0)
// gets no logon/lock events, hence the polling. Changed fires only when the
// value actually flips.
public sealed class SessionMonitor: IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private const int WtsSessionStateUnknown = -1;
    private const int WtsSessionStateUnlock = 1;

    private Timer? _timer;

    public bool IsSessionUnlocked { get; private set; }

    public event Action? Changed;

    public void Start()
    {
        // First poll runs synchronously so the property holds a real value
        // before the caller publishes the initial state.
        Poll(raiseChanged: false);
        _timer = new Timer(_ => Poll(raiseChanged: true), null, PollInterval, PollInterval);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void Poll(bool raiseChanged)
    {
        var unlocked = QuerySessionUnlocked();

        if (unlocked == IsSessionUnlocked)
            return;

        IsSessionUnlocked = unlocked;

        if (raiseChanged)
            Changed?.Invoke();
    }

    private static bool QuerySessionUnlocked()
    {
        var sessionId = WTSGetActiveConsoleSessionId();

        // 0xFFFFFFFF = no console session attached (e.g. during a session
        // switch); the logon screen's session has no user name yet.
        if (sessionId == 0xFFFFFFFF || string.IsNullOrEmpty(QuerySessionUserName(sessionId)))
            return false;

        return QuerySessionFlags(sessionId) == WtsSessionStateUnlock;
    }

    private static int QuerySessionFlags(uint sessionId)
    {
        if (!WTSQuerySessionInformationW(IntPtr.Zero, sessionId, WtsInfoClass.SessionInfoEx, out var buffer, out _))
            return WtsSessionStateUnknown;

        try
        {
            // WTSINFOEXW: Level at offset 0, the level-1 payload starts at 8
            // (8-byte alignment), SessionFlags follows SessionId and
            // SessionState. Verified empirically on Windows 11: 1 = unlocked,
            // 0 = locked — the documented values; the Win7-era "reversed
            // flags" defect does not apply here.
            return Marshal.ReadInt32(buffer, 16);
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    private static string? QuerySessionUserName(uint sessionId)
    {
        if (!WTSQuerySessionInformationW(IntPtr.Zero, sessionId, WtsInfoClass.UserName, out var buffer, out _))
            return null;

        try
        {
            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    private enum WtsInfoClass
    {
        UserName = 5,
        SessionInfoEx = 25
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQuerySessionInformationW(IntPtr server, uint sessionId, WtsInfoClass infoClass,
        out IntPtr buffer, out uint bytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);
}
