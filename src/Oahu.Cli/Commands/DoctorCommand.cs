using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Oahu.Cli.App.Doctor;
using Oahu.Cli.App.Errors;

namespace Oahu.Cli.Commands;

/// <summary>
/// <c>oahu-cli doctor</c> — environment self-checks.
/// </summary>
public static class DoctorCommand
{
    public static Command Create(Func<ParseResult, GlobalOptions> resolveGlobals, Func<ILoggerFactory> loggerFactory)
    {
        var jsonOpt = new Option<bool>("--json")
        {
            Description = "Emit machine-readable JSON instead of the pretty report.",
        };

        var skipNetworkOpt = new Option<bool>("--skip-network")
        {
            Description = "Skip the Audible API reachability probe (offline / CI).",
        };

        var fixOpt = new Option<bool>("--fix")
        {
            Description = "(Reserved) attempt to repair recoverable findings. Phase 1 prints what would be fixed.",
        };

        var printConfigOpt = new Option<bool>("--print-config")
        {
            Description = "Print the resolved CLI paths (config dir, log dir, token path, lock path) and exit. Useful for support and debugging.",
        };

        var cmd = new Command("doctor", "Run environment self-checks and exit non-zero if any error is found.")
        {
            jsonOpt,
            skipNetworkOpt,
            fixOpt,
            printConfigOpt,
        };

        cmd.SetAction(async (parse, ct) =>
        {
            var globals = resolveGlobals(parse);

            if (parse.GetValue(printConfigOpt))
            {
                PrintResolvedPaths(parse.GetValue(jsonOpt));
                return ExitCodes.Success;
            }

            using var lf = loggerFactory();
            var logger = lf.CreateLogger<DoctorService>();
            var service = new DoctorService(logger);

            var report = await service.RunAsync(
                new DoctorOptions { SkipNetwork = parse.GetValue(skipNetworkOpt) },
                ct).ConfigureAwait(false);

            if (parse.GetValue(jsonOpt))
            {
                DoctorRender.Json(report);
            }
            else
            {
                DoctorRender.Pretty(report, globals);
            }

            if (parse.GetValue(fixOpt) && report.HasErrors)
            {
                CliEnvironment.Error.WriteLine();
                CliEnvironment.Error.WriteLine("--fix is reserved: no auto-repair actions are implemented yet.");
            }

            return report.HasErrors ? ExitCodes.GenericFailure : ExitCodes.Success;
        });

        return cmd;
    }

    private static void PrintResolvedPaths(bool asJson)
    {
        var paths = new System.Collections.Generic.Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["configDir"] = Oahu.Cli.App.Paths.CliPaths.ConfigDir,
            ["logDir"] = Oahu.Cli.App.Paths.CliPaths.LogDir,
            ["sharedUserDataDir"] = Oahu.Cli.App.Paths.CliPaths.SharedUserDataDir,
            ["defaultDownloadDir"] = Oahu.Cli.App.Paths.CliPaths.DefaultDownloadDir,
            ["serverTokenPath"] = System.IO.Path.Combine(Oahu.Cli.App.Paths.CliPaths.ConfigDir, "server.token"),
            ["serverLockPath"] = System.IO.Path.Combine(Oahu.Cli.App.Paths.CliPaths.SharedUserDataDir, "server.lock"),
            ["auditLogPath"] = System.IO.Path.Combine(Oahu.Cli.App.Paths.CliPaths.SharedUserDataDir, "logs", "server-audit.jsonl"),
        };

        if (asJson)
        {
            var obj = new System.Text.Json.Nodes.JsonObject
            {
                ["_schemaVersion"] = "1",
                ["resource"] = "doctor-config",
            };
            foreach (var kv in paths)
            {
                obj[kv.Key] = System.Text.Json.Nodes.JsonValue.Create(kv.Value?.ToString());
            }
            Console.WriteLine(obj.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return;
        }

        foreach (var kv in paths)
        {
            Console.WriteLine($"{kv.Key,-22}{kv.Value}");
        }
    }
}
