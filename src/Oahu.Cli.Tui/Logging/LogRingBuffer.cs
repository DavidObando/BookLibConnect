using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Oahu.Cli.Tui.Logging;

/// <summary>One captured log record, ready to render in the Logs overlay.</summary>
public readonly record struct LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Category, string Message, string? Exception)
{
    public string FormatLine()
    {
        var lvl = Level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???",
        };
        var head = $"{Timestamp:HH:mm:ss.fff} {lvl} [{Category}] {Message}";
        return Exception is null ? head : head + " — " + Exception;
    }
}

/// <summary>
/// In-memory ring buffer of <see cref="LogEntry"/> records that doubles as an
/// <see cref="ILoggerProvider"/>. Used by the TUI Logs overlay so that the user
/// can press <c>L</c> and inspect logs that were emitted while they were
/// looking at another screen.
///
/// Thread-safe: writes lock the ring; reads take a snapshot.
/// </summary>
public sealed class LogRingBuffer : ILoggerProvider
{
    /// <summary>Default capacity (entries). Older entries are dropped on overflow.</summary>
    public const int DefaultCapacity = 500;

    private readonly object writeLock = new();
    private readonly LogEntry[] ring;
    private readonly LogLevel minimumLevel;
    private readonly ConcurrentDictionary<string, RingLogger> loggers = new();
    private int head;
    private int count;
    private bool disposed;

    public LogRingBuffer(int capacity = DefaultCapacity, LogLevel minimumLevel = LogLevel.Information)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
        }
        ring = new LogEntry[capacity];
        this.minimumLevel = minimumLevel;
    }

    public int Capacity => ring.Length;

    public int Count
    {
        get
        {
            lock (writeLock)
            {
                return count;
            }
        }
    }

    public LogLevel MinimumLevel => minimumLevel;

    public ILogger CreateLogger(string categoryName) =>
        loggers.GetOrAdd(categoryName, name => new RingLogger(name, this));

    /// <summary>
    /// Append an entry. Older entries are evicted when the ring is full.
    /// </summary>
    public void Append(LogEntry entry)
    {
        if (disposed)
        {
            return;
        }
        lock (writeLock)
        {
            ring[head] = entry;
            head = (head + 1) % ring.Length;
            if (count < ring.Length)
            {
                count++;
            }
        }
    }

    /// <summary>
    /// Returns a snapshot of the entries in chronological order (oldest first).
    /// </summary>
    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (writeLock)
        {
            var result = new LogEntry[count];
            // Oldest entry sits at (head - count) mod len.
            var start = (head - count + ring.Length) % ring.Length;
            for (var i = 0; i < count; i++)
            {
                result[i] = ring[(start + i) % ring.Length];
            }
            return result;
        }
    }

    public void Clear()
    {
        lock (writeLock)
        {
            head = 0;
            count = 0;
        }
    }

    public void Dispose()
    {
        disposed = true;
    }

    private sealed class RingLogger : ILogger
    {
        private readonly string category;
        private readonly LogRingBuffer owner;

        public RingLogger(string category, LogRingBuffer owner)
        {
            this.category = category;
            this.owner = owner;
        }

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= owner.minimumLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }
            ArgumentNullException.ThrowIfNull(formatter);
            var msg = formatter(state, exception);
            owner.Append(new LogEntry(DateTimeOffset.Now, logLevel, category, msg, exception?.ToString()));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose() { }
        }
    }
}
