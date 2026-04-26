using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Linq;
using System.Threading.Tasks;
using Oahu.Cli.App.Errors;
using Oahu.Cli.App.Models;
using Oahu.Cli.Output;

namespace Oahu.Cli.Commands;

/// <summary>
/// <c>oahu-cli convert &lt;asin&gt;...</c>.
///
/// <para>
/// Convenience alias around <see cref="DownloadCommand"/> that always sets
/// <see cref="JobRequest.ExportToAax"/>. The underlying
/// <see cref="Oahu.Core.DownloadDecryptJob{T}"/> short-circuits download/decrypt
/// when the local file already exists, so re-invoking <c>convert</c> on a
/// previously-downloaded book just runs the AAX export step. If the book has
/// not been downloaded, <c>convert</c> downloads + decrypts + exports.
/// </para>
///
/// <para>
/// Accepts ASINs as positional args. As a convenience, if a positional arg is
/// a path to a local <c>.aax</c> / <c>.aaxc</c> file whose name matches the
/// Audible naming convention <c>&lt;ASIN&gt;_xxx.aax[c]</c>, the ASIN is
/// inferred from the filename and the file is converted in-place. Other
/// file-based convert flows are not supported because <see cref="Oahu.Core.AaxExporter"/>
/// requires per-book license metadata that is keyed by ASIN.
/// </para>
/// </summary>
public static class ConvertCommand
{
    public static Command Create(Func<ParseResult, GlobalOptions> resolveGlobals)
    {
        var asinArg = new Argument<string[]>("asin")
        {
            Arity = ArgumentArity.ZeroOrMore,
            Description = "One or more ASINs (or paths to <ASIN>_xxx.aax files). Use '-' to read one ASIN per line from stdin.",
        };
        var outputDirOpt = new Option<string?>("--output-dir")
        {
            Description = "Override the export directory.",
        };
        var profileOpt = new Option<string?>("--profile")
        {
            Description = "Profile alias to use (defaults to the active profile).",
        };
        var concurrencyOpt = new Option<int?>("--concurrency")
        {
            Description = "Maximum parallel jobs (default: 1). Must be >= 1.",
        };

        var cmd = new Command(
            "convert",
            "Export one or more audiobooks to AAX. Downloads and decrypts first if needed.")
        {
            asinArg,
            outputDirOpt,
            profileOpt,
            concurrencyOpt,
        };

        cmd.SetAction(async (parse, ct) =>
        {
            var globals = resolveGlobals(parse);
            var asins = parse.GetValue(asinArg) ?? Array.Empty<string>();
            var outputDir = parse.GetValue(outputDirOpt);
            var profile = parse.GetValue(profileOpt);
            var concurrency = parse.GetValue(concurrencyOpt);
            if (concurrency is { } cVal && cVal < 1)
            {
                CliEnvironment.Error.WriteLine("oahu-cli: --concurrency must be >= 1.");
                return ExitCodes.UsageError;
            }

            var distinct = asins
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(TryResolveAsinFromPath)
                .Where(s => s.Resolved is not null)
                .Select(s => s.Resolved!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            // Surface any inputs we couldn't resolve to an ASIN so the user
            // gets a concrete error rather than silent dropping.
            var unresolved = asins
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .Where(s => TryResolveAsinFromPath(s).Resolved is null)
                .ToArray();
            if (unresolved.Length > 0)
            {
                CliEnvironment.Error.WriteLine(
                    $"oahu-cli: could not infer ASIN from: {string.Join(", ", unresolved)}. "
                    + "Pass an ASIN, or a file named '<ASIN>_xxx.aax[c]'.");
                return ExitCodes.UsageError;
            }

            if (distinct.Length == 0)
            {
                CliEnvironment.Error.WriteLine("oahu-cli: no ASINs supplied.");
                return ExitCodes.UsageError;
            }

            var requests = distinct
                .Select(a => new JobRequest
                {
                    Asin = a,
                    Title = a,
                    ProfileAlias = profile,
                    Quality = DownloadQuality.High,
                    ExportToAax = true,
                    OutputDir = outputDir,
                })
                .ToList();

            var writer = OutputWriterFactory.Create(ConfigCommand.BuildContext(globals));

            if (globals.DryRun)
            {
                DownloadCommand.EmitDryRunPlan(writer, requests);
                return ExitCodes.Success;
            }

            if (concurrency is { } cParallelism)
            {
                CliServiceFactory.OverrideMaxParallelism = cParallelism;
            }
            var jobService = CliServiceFactory.JobServiceFactory();
            return await DownloadCommand.RunAsync(jobService, requests, writer, ct).ConfigureAwait(false);
        });

        return cmd;
    }

    /// <summary>
    /// Maps a positional input — either an ASIN or a path to an Audible
    /// <c>&lt;ASIN&gt;_xxx.aax[c]</c> file — to the underlying ASIN. We treat
    /// the input as a path only if it contains a directory separator or ends
    /// with an Audible file extension; otherwise we accept it as an ASIN
    /// verbatim and let the executor reject any unknown ASIN.
    /// Returns <c>(input, null)</c> when neither form yields an ASIN.
    /// </summary>
    internal static (string Input, string? Resolved) TryResolveAsinFromPath(string raw)
    {
        var looksLikePath =
            raw.IndexOfAny(new[] { '/', '\\' }) >= 0
            || raw.EndsWith(".aax", StringComparison.OrdinalIgnoreCase)
            || raw.EndsWith(".aaxc", StringComparison.OrdinalIgnoreCase);

        if (!looksLikePath)
        {
            return (raw, raw);
        }

        try
        {
            var fileName = System.IO.Path.GetFileName(raw);
            if (!string.IsNullOrEmpty(fileName))
            {
                var underscore = fileName.IndexOf('_');
                if (underscore > 0)
                {
                    var candidate = fileName.Substring(0, underscore);
                    if (LooksLikeAsin(candidate))
                    {
                        return (raw, candidate.ToUpperInvariant());
                    }
                }
            }
        }
        catch (ArgumentException)
        {
            // GetFileName throws on invalid path chars; treat as unresolved.
        }

        return (raw, null);
    }

    private static bool LooksLikeAsin(string s)
    {
        if (s is not { Length: 10 })
        {
            return false;
        }
        foreach (var c in s)
        {
            if (!char.IsLetterOrDigit(c))
            {
                return false;
            }
        }
        return true;
    }
}
