using Serilog.Core;
using Serilog.Events;

namespace DeltaCrafter.App.Services;

public sealed record UiLogLine(DateTimeOffset At, LogEventLevel Level, string Message);

/// <summary>
/// Serilog → 日志页的桥。环形缓冲(2000 条)防内存无限增长;
/// Emitted 可能在任意线程触发,订阅方负责切回 UI 线程。
/// </summary>
public sealed class UiLogSink : ILogEventSink
{
    private const int Capacity = 2000;
    private readonly object _gate = new();
    private readonly Queue<UiLogLine> _buffer = new();

    public event Action<UiLogLine>? Emitted;

    public void Emit(LogEvent logEvent)
    {
        string message = logEvent.RenderMessage();
        if (logEvent.Exception is not null)
            message += " | " + logEvent.Exception.Message;

        var line = new UiLogLine(logEvent.Timestamp, logEvent.Level, message);
        lock (_gate)
        {
            _buffer.Enqueue(line);
            while (_buffer.Count > Capacity) _buffer.Dequeue();
        }
        Emitted?.Invoke(line);
    }

    public IReadOnlyList<UiLogLine> Snapshot()
    {
        lock (_gate) return _buffer.ToList();
    }
}
