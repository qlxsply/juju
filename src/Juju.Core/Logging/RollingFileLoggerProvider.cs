using Microsoft.Extensions.Logging;

namespace Juju.Core.Logging;

/// <summary>
/// 按 UTC 日期分文件的最小日志提供程序。主构造函数的 <c>directory</c> 被传给每个分类日志器，
/// 不依赖全局可变状态。
/// </summary>
public sealed class RollingFileLoggerProvider(string directory) : ILoggerProvider
{
    // 多个 ILogger 可能在不同线程写入同一个日文件，因此共享同一把同步锁。
    private readonly object _gate = new();

    /// <summary>为日志分类创建共享锁的文件日志器。</summary>
    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, directory, _gate);

    /// <summary>提供程序不持有长期资源；各次写入自行打开并关闭文件。</summary>
    public void Dispose()
    {
    }

    /// <summary>执行实际同步追加写入的分类日志器。</summary>
    private sealed class FileLogger(string category, string directory, object gate) : ILogger
    {
        /// <summary>此轻量实现不支持日志作用域。</summary>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        /// <summary>仅记录 Information 及更高等级，过滤低价值诊断日志。</summary>
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        /// <summary>
        /// 格式化单行并在 <c>lock</c> 内追加，防止线程间字节交错。与 Java synchronized 类似，
        /// 但锁只覆盖短暂同步文件 IO，不能跨 <c>await</c> 使用。
        /// </summary>
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