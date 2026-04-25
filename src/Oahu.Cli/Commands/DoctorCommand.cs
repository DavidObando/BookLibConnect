using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Oahu.Cli.App.Doctor;

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

        var cmd = new Command("doctor", "Run environment self-checks and exit non-zero if any error is found.")
        {
            jsonOpt,
            skipNetworkOpt,
            fixOpt,
        };

        cmd.SetAction(async (parse, ct) =>
        {
            var globals = resolveGlobals(parse);

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

            return report.HasErrors ? 1 : 0;
        });

        return cmd;
    }
}
