using System;
using System.Collections.Concurrent;
using System.IO;
using Microsoft.Extensions.Logging;
using Oahu.Cli.App.Paths;

namespace Oahu.Cli.Logging;

/// <summary>
/// Daily-rotating file <see cref="ILoggerProvider"/> that writes to
/// <c>&lt;CliPaths.LogDir&gt;/oahu-cli-YYYYMMDD.log</c>.
///
/// Phase 1 deliberately uses a tiny self-contained provider (no Serilog / NLog
/// dependency) — Phase 7 may swap this for Serilog if structured sinks become
/// useful for the Logs overlay.
///
/// Thread-safe: writes are serialised through a single <see cref="lock"/>; the
/// log file is rotated lazily when the date changes.
/// </summary>
public sealed class RotatingFileLoggerProvider : ILoggerProvider
{
    private readonly LogLevel minimumLevel;
    private readonly string directory;
    private readonly object writeLock = new();
    private readonly ConcurrentDictionary<string, RotatingFileLogger> loggers = new();
    private DateOnly currentDate;
    private StreamWriter? writer;
    private bool disposed;

    public RotatingFileLoggerProvider(LogLevel minimumLevel = LogLevel.Information, string? directory = null)
    {
        this.minimumLevel = minimumLevel;
        this.directory = directory ?? CliPaths.LogDir;
    }

    public ILogger CreateLogger(string categoryName) =>
        loggers.GetOrAdd(categoryName, name => new RotatingFileLogger(name, this));

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        lock (writeLock)
        {
            try
            {
                writer?.Flush();
                writer?.Dispose();
            }
            catch
            {
                // best effort
            }
            writer = null;
        }
    }

    internal void Write(string category, LogLevel level, EventId eventId, string message, Exception? exception)
    {
        if (level < minimumLevel || disposed)
        {
            return;
        }

        var ts = DateTimeOffset.Now;
        var line = FormatLine(ts, level, category, eventId, message, exception);

        lock (writeLock)
        {
            try
            {
                EnsureWriterFor(ts.Date);
                writer!.WriteLine(line);
                if (level >= LogLevel.Warning)
                {
                    writer.Flush();
                }
            }
            catch
            {
                // Logging must never crash the CLI. Drop silently.
            }
        }
    }

    internal LogLevel MinimumLevel => minimumLevel;

    private static string FormatLine(DateTimeOffset ts, LogLevel level, string category, EventId eventId, string message, Exception? exception)
    {
        var lvl = level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???",
        };
        var head = $"{ts:yyyy-MM-ddTHH:mm:ss.fffzzz} {lvl} [{category}]";
        if (eventId.Id != 0 || !string.IsNullOrEmpty(eventId.Name))
        {
            head += $" ({eventId.Id}/{eventId.Name})";
        }
        var body = exception is null
            ? $"{head} {message}"
            : $"{head} {message}{Environment.NewLine}{exception}";
        return body;
    }

    private void EnsureWriterFor(DateTime localDate)
    {
        var d = DateOnly.FromDateTime(localDate);
        if (writer is not null && d == currentDate)
        {
            return;
        }

        try
        {
            writer?.Flush();
            writer?.Dispose();
        }
        catch
        {
            // ignore
        }

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"oahu-cli-{d:yyyyMMdd}.log");
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        writer = new StreamWriter(stream) { AutoFlush = false };
        currentDate = d;
    }

    private sealed class RotatingFileLogger : ILogger
    {
        private readonly string category;
        private readonly RotatingFileLoggerProvider provider;

        public RotatingFileLogger(string category, RotatingFileLoggerProvider provider)
        {
            this.category = category;
            this.provider = provider;
        }

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= provider.MinimumLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }
            var msg = formatter(state, exception);
            provider.Write(category, logLevel, eventId, msg, exception);
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
