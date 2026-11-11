using Microsoft.Extensions.Logging;
using Timer = System.Threading.Timer;

namespace HomeAssistant.Agent.Plugin.MacOS.Common;

// Runs a check on a timer, one at a time: a slow check (a hung ioreg) skips the ticks it overlaps instead of piling them up.
internal sealed class Poller : IDisposable
{
    private readonly Action _poll;
    private readonly ILogger _logger;
    private readonly Timer _timer;

    private int _busy;

    public Poller(TimeSpan interval, Action poll, ILogger logger)
    {
        _poll = poll;
        _logger = logger;
        _timer = new Timer(_ => Tick(), null, interval, interval);
    }

    public void Dispose()
    {
        _timer.Dispose();
    }

    private void Tick()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
            return;

        try
        {
            _poll();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Poll failed");
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }
}
