using System;
using System.CommandLine;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Oahu.Cli.Commands;
using Oahu.Cli.Logging;

namespace Oahu.Cli;

/// <summary>
/// Entry point for <c>oahu-cli</c>.
///
/// Phase 1 deliverables (see <c>docs/OAHU_CLI_DESIGN.md</c> §Phase 1):
///   • <c>--version</c>, <c>--help</c>
///   • <c>oahu-cli doctor</c>
///   • Process setup: NO_COLOR / FORCE_COLOR, UTF-8, redirect detection, exit-trap
///   • Daily-rotating file logger
///   • TUI placeholder (refuses to enter, returns a clear "not yet implemented" message)
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        CliEnvironment.Initialise();

        var minLevel = ResolveMinLogLevel(args);
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(minLevel);
            builder.AddProvider(new RotatingFileLoggerProvider(minLevel));
        });

        var root = RootCommandFactory.Create(() => loggerFactory);

        try
        {
            var parseResult = root.Parse(args);
            var rewriteCode = Commands.ParseErrorRewriter.RewriteIfNeeded(parseResult, CliEnvironment.Error);
            if (rewriteCode is int code)
            {
                return code;
            }
            return await parseResult.InvokeAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C in command mode (per §10): exit code 130.
            return 130;
        }
        catch (Exception ex)
        {
            // Last-resort: never let an exception escape uncaught.
            try
            {
                CliEnvironment.Error.WriteLine($"oahu-cli: {ex.GetType().Name}: {ex.Message}");
                CliEnvironment.Error.WriteLine("(Run `oahu-cli doctor` to verify your environment, or check the daily log under the logs directory.)");
            }
            catch
            {
                // ignore secondary failures
            }
            return 1;
        }
        finally
        {
            CliEnvironment.RunRestore();
        }
    }

    private static LogLevel ResolveMinLogLevel(string[] args)
    {
        // Quick prescan — final --log-level wins, --verbose adds Debug, otherwise Information.
        string? raw = null;
        bool verbose = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--log-level" && i + 1 < args.Length)
            {
                raw = args[i + 1];
            }
            else if (args[i].StartsWith("--log-level=", StringComparison.Ordinal))
            {
                raw = args[i].Substring("--log-level=".Length);
            }
            else if (args[i] == "--verbose")
            {
                verbose = true;
            }
        }

        if (!string.IsNullOrEmpty(raw))
        {
            return raw.ToLowerInvariant() switch
            {
                "trace" => LogLevel.Trace,
                "debug" => LogLevel.Debug,
                "information" or "info" => LogLevel.Information,
                "warning" or "warn" => LogLevel.Warning,
                "error" => LogLevel.Error,
                "critical" or "crit" => LogLevel.Critical,
                "none" or "off" => LogLevel.None,
                _ => LogLevel.Information,
            };
        }

        return verbose ? LogLevel.Debug : LogLevel.Information;
    }
}
