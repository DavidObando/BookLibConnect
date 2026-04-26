using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Threading.Tasks;
using Oahu.Cli.App.Config;
using Oahu.Cli.App.Errors;
using Oahu.Cli.App.Models;
using Oahu.Cli.App.Paths;
using Oahu.Cli.Output;
using Spectre.Console;

namespace Oahu.Cli.Commands;

/// <summary>
/// <c>oahu-cli config get|set|path</c> — inspect and update the user's CLI config.
/// </summary>
public static class ConfigCommand
{
    /// <summary>Stable string keys (kebab-case) exposed to users via <c>config get/set</c>.</summary>
    public static readonly IReadOnlyList<string> Keys = new[]
    {
        "download-dir",
        "default-quality",
        "max-parallel-jobs",
        "keep-encrypted-files",
        "multi-part-download",
        "export-to-aax",
        "export-dir",
        "default-profile-alias",
        "allow-encrypted-file-credentials",
        "theme",
    };

    public static Command Create(Func<ParseResult, GlobalOptions> resolveGlobals)
    {
        var cmd = new Command("config", "Inspect and update CLI configuration.");
        cmd.Subcommands.Add(CreateGet(resolveGlobals));
        cmd.Subcommands.Add(CreateSet(resolveGlobals));
        cmd.Subcommands.Add(CreatePath(resolveGlobals));
        return cmd;
    }

    public static IReadOnlyDictionary<string, object?> ToDictionary(OahuConfig cfg) => new Dictionary<string, object?>
    {
        ["download-dir"] = cfg.DownloadDirectory,
        ["default-quality"] = cfg.DefaultQuality.ToString(),
        ["max-parallel-jobs"] = cfg.MaxParallelJobs,
        ["keep-encrypted-files"] = cfg.KeepEncryptedFiles,
        ["multi-part-download"] = cfg.MultiPartDownload,
        ["export-to-aax"] = cfg.ExportToAax,
        ["export-dir"] = cfg.ExportDirectory,
        ["default-profile-alias"] = cfg.DefaultProfileAlias,
        ["allow-encrypted-file-credentials"] = cfg.AllowEncryptedFileCredentials,
        ["theme"] = cfg.Theme,
    };

    public static OahuConfig ApplySetting(OahuConfig cfg, string key, string value) => key switch
    {
        "download-dir" => cfg with { DownloadDirectory = value },
        "default-quality" => cfg with { DefaultQuality = ParseQuality(value) },
        "max-parallel-jobs" => cfg with { MaxParallelJobs = ParsePositive(value) },
        "keep-encrypted-files" => cfg with { KeepEncryptedFiles = ParseBool(value) },
        "multi-part-download" => cfg with { MultiPartDownload = ParseBool(value) },
        "export-to-aax" => cfg with { ExportToAax = ParseBool(value) },
        "export-dir" => cfg with { ExportDirectory = value },
        "default-profile-alias" => cfg with { DefaultProfileAlias = string.IsNullOrEmpty(value) ? null : value },
        "allow-encrypted-file-credentials" => cfg with { AllowEncryptedFileCredentials = ParseBool(value) },
        "theme" => cfg with { Theme = ParseTheme(value) },
        _ => throw new ArgumentException($"Unknown config key '{key}'. Valid: {string.Join(", ", Keys)}"),
    };

    private static Command CreateGet(Func<ParseResult, GlobalOptions> resolveGlobals)
    {
        var keyArg = new Argument<string?>("key") { Arity = ArgumentArity.ZeroOrOne, Description = "Specific key to read; omit to dump all." };
        var get = new Command("get", "Read a single key, or dump every key when no key is given.") { keyArg };
        get.SetAction(async (parse, ct) =>
        {
            var globals = resolveGlobals(parse);
            var key = parse.GetValue(keyArg);
            var path = ResolveConfigFile(globals);
            var svc = new JsonConfigService(path);
            var cfg = await svc.LoadAsync(ct).ConfigureAwait(false);
            var writer = OutputWriterFactory.Create(BuildContext(globals));

            if (string.IsNullOrEmpty(key))
            {
                writer.WriteResource("config", ToDictionary(cfg));
                return ExitCodes.Success;
            }

            var dict = ToDictionary(cfg);
            if (!dict.TryGetValue(key, out var value))
            {
                CliEnvironment.Error.WriteLine($"oahu-cli: unknown config key '{key}'.");
                CliEnvironment.Error.WriteLine($"Known keys: {string.Join(", ", Keys)}");
                return ExitCodes.UsageError;
            }
            writer.WriteResource("config-value", new Dictionary<string, object?> { ["key"] = key, ["value"] = value });
            return ExitCodes.Success;
        });
        return get;
    }

    private static Command CreateSet(Func<ParseResult, GlobalOptions> resolveGlobals)
    {
        var keyArg = new Argument<string>("key") { Description = "Key to update (one of the documented config keys)." };
        var valueArg = new Argument<string>("value") { Description = "New value (use the empty string to clear nullable keys)." };
        var set = new Command("set", "Update a single config key and persist atomically.") { keyArg, valueArg };
        set.SetAction(async (parse, ct) =>
        {
            var globals = resolveGlobals(parse);
            var key = parse.GetValue(keyArg)!;
            var value = parse.GetValue(valueArg) ?? string.Empty;
            var path = ResolveConfigFile(globals);
            var svc = new JsonConfigService(path);
            var cfg = await svc.LoadAsync(ct).ConfigureAwait(false);
            OahuConfig updated;
            try
            {
                updated = ApplySetting(cfg, key, value);
            }
            catch (ArgumentException ex)
            {
                CliEnvironment.Error.WriteLine($"oahu-cli: {ex.Message}");
                return ExitCodes.UsageError;
            }
            await svc.SaveAsync(updated, ct).ConfigureAwait(false);
            var writer = OutputWriterFactory.Create(BuildContext(globals));
            writer.WriteSuccess($"Set {key} = {value}");
            return ExitCodes.Success;
        });
        return set;
    }

    private static Command CreatePath(Func<ParseResult, GlobalOptions> resolveGlobals)
    {
        var path = new Command("path", "Print the absolute path to the active config file.");
        path.SetAction(parse =>
        {
            var globals = resolveGlobals(parse);
            var p = ResolveConfigFile(globals);
            var writer = OutputWriterFactory.Create(BuildContext(globals));
            writer.WriteResource("config-path", new Dictionary<string, object?> { ["path"] = p });
            return ExitCodes.Success;
        });
        return path;
    }

    public static string ResolveConfigFile(GlobalOptions globals)
    {
        var dir = !string.IsNullOrEmpty(globals.ConfigDirOverride) ? globals.ConfigDirOverride! : CliPaths.ConfigDir;
        return Path.Combine(dir, "config.json");
    }

    public static OutputContext BuildContext(GlobalOptions g) => new(
        OutputContext.ResolveFormat(g.Json, g.Plain, !CliEnvironment.IsStdoutTty),
        g.Quiet,
        useColor: !g.ForceNoColor && !CliEnvironment.ColorDisabled,
        useAscii: g.UseAscii);

    private static DownloadQuality ParseQuality(string s) => s.ToLowerInvariant() switch
    {
        "extreme" => DownloadQuality.Extreme,
        "high" => DownloadQuality.High,
        "normal" => DownloadQuality.Normal,
        _ => throw new ArgumentException($"Invalid quality '{s}'. Valid: extreme, high, normal."),
    };

    private static bool ParseBool(string s) => s.ToLowerInvariant() switch
    {
        "true" or "1" or "yes" or "on" => true,
        "false" or "0" or "no" or "off" or "" => false,
        _ => throw new ArgumentException($"Invalid boolean '{s}'. Valid: true|false|yes|no|on|off|1|0."),
    };

    private static int ParsePositive(string s) => int.TryParse(s, out var n) && n > 0
        ? n
        : throw new ArgumentException($"Invalid positive integer '{s}'.");

    private static string? ParseTheme(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return null;
        }
        foreach (var name in Oahu.Cli.Tui.Themes.Theme.AvailableNames())
        {
            if (string.Equals(name, s, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }
        throw new ArgumentException(
            $"Invalid theme '{s}'. Valid: {string.Join(", ", Oahu.Cli.Tui.Themes.Theme.AvailableNames())} (or empty to clear).");
    }
}
