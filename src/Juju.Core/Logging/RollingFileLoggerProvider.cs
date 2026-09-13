using Microsoft.Extensions.Logging;

namespace Juju.Core.Logging;

public sealed class RollingFileLoggerProvider(string directory) : ILoggerProvider
{
    private readonly object _gate = new();
    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, directory, _gate);

    public void Dispose()
    {
    }

    private sealed class FileLogger(string category, string directory, object gate) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Directory.CreateDirectory(directory);
            var line =
                $"{DateTimeOffset.UtcNow:O} [{level}] {category}: {formatter(state, exception)}{Environment.NewLine}";
            lock (gate) File.AppendAllText(Path.Combine(directory, $"juju-{DateTime.UtcNow:yyyyMMdd}.log"), line);
        }
    }
}