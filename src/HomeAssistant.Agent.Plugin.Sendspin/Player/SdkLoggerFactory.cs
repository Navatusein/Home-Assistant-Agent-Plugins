using Microsoft.Extensions.Logging;

namespace HomeAssistant.Agent.Plugin.Sendspin.Player;

// The loggers the Sendspin SDK writes through. Its categories ("Sendspin.SDK.
// Client.SendspinHostService") are moved under the plugin's namespace, which is
// how the agent tells a plugin's log apart: the lines read
// "sendspin-plugin/SendspinHostService" and follow the plugin's log level.
//
// The SDK logs every mDNS query and whole protocol messages as Information;
// that is Debug here, so the plugin's default level shows what the player
// does (connected, paired, playing) and not how.
internal sealed class SdkLoggerFactory(ILoggerFactory inner) : ILoggerFactory
{
    private const string Prefix = "HomeAssistant.Agent.Plugin.Sendspin.Sdk.";

    public ILogger CreateLogger(string categoryName) =>
        new SdkLogger(inner.CreateLogger(Prefix + categoryName.Replace("Sendspin.SDK.", "", StringComparison.Ordinal)));

    public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);

    // The host owns the factory.
    public void Dispose() { }

    private sealed class SdkLogger(ILogger inner) : ILogger
    {
        private static LogLevel Map(LogLevel level) => level == LogLevel.Information ? LogLevel.Debug : level;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(Map(logLevel));

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            inner.Log(Map(logLevel), eventId, state, exception, formatter);
    }
}
