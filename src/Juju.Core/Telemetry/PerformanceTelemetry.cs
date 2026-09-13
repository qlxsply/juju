using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Juju.Core.Telemetry;

public enum PerformanceOperation
{
    Startup,
    Launcher,
    WebViewInitialization,
    MonacoInitialization,
    DocumentLoad,
    DocumentSave,
    DocumentImport,
    DocumentReconcile
}

public sealed record PerformanceMeasurement(
    PerformanceOperation Operation,
    TimeSpan Duration,
    long WorkingSetBytes,
    long ManagedMemoryBytes);

public interface IPerformanceTelemetry
{
    IDisposable Measure(PerformanceOperation operation);
    void RecordMemory(PerformanceOperation operation);
}

public sealed class PerformanceTelemetry(ILogger<PerformanceTelemetry> logger) : IPerformanceTelemetry
{
    public IDisposable Measure(PerformanceOperation operation) => new Measurement(operation, this);

    public void RecordMemory(PerformanceOperation operation) => Record(operation, TimeSpan.Zero);

    private void Record(PerformanceOperation operation, TimeSpan duration)
    {
        var process = Process.GetCurrentProcess();
        logger.LogInformation(
            "Performance {Operation}: {DurationMs} ms, working set {WorkingSetBytes} bytes, managed memory {ManagedMemoryBytes} bytes",
            operation,
            duration.TotalMilliseconds,
            process.WorkingSet64,
            GC.GetTotalMemory(forceFullCollection: false));
    }

    private sealed class Measurement(PerformanceOperation operation, PerformanceTelemetry telemetry) : IDisposable
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) telemetry.Record(operation, _stopwatch.Elapsed);
        }
    }
}

public sealed class NullPerformanceTelemetry : IPerformanceTelemetry
{
    public static NullPerformanceTelemetry Instance { get; } = new();

    private NullPerformanceTelemetry()
    {
    }

    public IDisposable Measure(PerformanceOperation operation) => EmptyDisposable.Instance;

    public void RecordMemory(PerformanceOperation operation)
    {
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static EmptyDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}