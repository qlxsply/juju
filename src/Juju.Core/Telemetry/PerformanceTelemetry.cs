using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Juju.Core.Telemetry;

/// <summary>可测量的用户可感知操作类别，作为结构化日志字段而非自由文本。</summary>
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

/// <summary>一次性能采样的值对象；record 的值相等性适合后续比较或聚合。</summary>
public sealed record PerformanceMeasurement(
    PerformanceOperation Operation,
    TimeSpan Duration,
    long WorkingSetBytes,
    long ManagedMemoryBytes);

/// <summary>性能遥测抽象，使业务代码不依赖具体日志实现。</summary>
public interface IPerformanceTelemetry
{
    /// <summary>
    /// 开始计时并返回作用域对象；应使用 <c>using</c>，其退出时会记录耗时，
    /// 对应 Java try-with-resources 的自动关闭模式。
    /// </summary>
    IDisposable Measure(PerformanceOperation operation);

    /// <summary>立即记录内存快照，不计算操作持续时间。</summary>
    void RecordMemory(PerformanceOperation operation);
}

/// <summary>
/// 将耗时和进程内存写入 <see cref="ILogger"/> 的遥测实现。主构造函数参数 <c>logger</c>
/// 会由 C# 直接捕获为实例状态，不需要 Java 中显式字段和构造函数赋值。
/// </summary>
public sealed class PerformanceTelemetry(ILogger<PerformanceTelemetry> logger) : IPerformanceTelemetry
{
    /// <summary>创建在释放时记录耗时的测量作用域。</summary>
    public IDisposable Measure(PerformanceOperation operation) => new Measurement(operation, this);

    /// <summary>记录零耗时的内存采样。</summary>
    public void RecordMemory(PerformanceOperation operation) => Record(operation, TimeSpan.Zero);

    /// <summary>读取当前进程工作集和托管堆估算值并输出结构化日志。</summary>
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

    /// <summary>一次性计时器；<see cref="Interlocked.Exchange(ref int, int)"/> 保证重复释放不会重复记账。</summary>
    private sealed class Measurement(PerformanceOperation operation, PerformanceTelemetry telemetry) : IDisposable
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private int _disposed;

        /// <summary>首次释放时停止计时并写入遥测；该同步释放不涉及异步资源。</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) telemetry.Record(operation, _stopwatch.Elapsed);
        }
    }
}

/// <summary>空对象实现，允许调用方无分支地保留测量代码而不产生遥测。</summary>
public sealed class NullPerformanceTelemetry : IPerformanceTelemetry
{
    public static NullPerformanceTelemetry Instance { get; } = new();

    private NullPerformanceTelemetry()
    {
    }

    /// <summary>返回无操作作用域以保持 <c>using</c> 调用模式。</summary>
    public IDisposable Measure(PerformanceOperation operation) => EmptyDisposable.Instance0;

    /// <summary>有意忽略内存采样。</summary>
    public void RecordMemory(PerformanceOperation operation)
    {
    }

    /// <summary>共享的无操作释放对象，避免每次测量分配实例。</summary>
    private sealed class EmptyDisposable : IDisposable
    {
        public static EmptyDisposable Instance0 { get; } = new();

        public void Dispose()
        {
        }
    }
}