using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Linq;
using System.Threading.Tasks;
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
/// The design (§4.1) lists <c>convert &lt;file&gt;</c>; we deliberately accept
/// ASINs instead because <see cref="Oahu.Core.AaxExporter"/> is
/// <c>Book</c>-driven (needs library metadata, chapters, and cover) and there
/// is no current path to reconstruct that from a bare file. A future
/// <c>library export</c> command could replace this if the file-based form
/// becomes important.
/// </para>
/// </summary>
public static class ConvertCommand
{
    public static Command Create(Func<ParseResult, GlobalOptions> resolveGlobals)
    {
        var asinArg = new Argument<string[]>("asin")
        {
            Arity = ArgumentArity.ZeroOrMore,
            Description = "One or more ASINs to convert. Use '-' to read one ASIN per line from stdin.",
        };
        var outputDirOpt = new Option<string?>("--output-dir")
        {
            Description = "Override the export directory.",
        };
        var profileOpt = new Option<string?>("--profile")
        {
            Description = "Profile alias to use (defaults to the active profile).",
        };

        var cmd = new Command(
            "convert",
            "Export one or more audiobooks to AAX. Downloads and decrypts first if needed.")
        {
            asinArg,
            outputDirOpt,
            profileOpt,
        };

        cmd.SetAction(async (parse, ct) =>
        {
            var globals = resolveGlobals(parse);
            var asins = parse.GetValue(asinArg) ?? Array.Empty<string>();
            var outputDir = parse.GetValue(outputDirOpt);
            var profile = parse.GetValue(profileOpt);

            var distinct = asins
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (distinct.Length == 0)
            {
                CliEnvironment.Error.WriteLine("oahu-cli: no ASINs supplied.");
                return 2;
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
            var jobService = CliServiceFactory.JobServiceFactory();
            return await DownloadCommand.RunAsync(jobService, requests, writer, ct).ConfigureAwait(false);
        });

        return cmd;
    }
}
