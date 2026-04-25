using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Linq;
using System.Threading.Tasks;
using Oahu.Cli.App.Auth;
using Oahu.Cli.App.Models;
using Oahu.Cli.Output;

namespace Oahu.Cli.Commands;

/// <summary>
/// <c>oahu-cli auth login | status | logout</c>.
///
/// 4b.1 ships the command surface against <see cref="IAuthService"/>. The default
/// resolver in <see cref="CliServiceFactory"/> returns a Fake, so on a clean
/// machine <c>auth status</c> reports "no profiles signed in" until 4b.2 wires the
/// Core-backed <c>CoreAuthService</c>. <c>auth login</c> runs the broker
/// round-trip (URL prompt → user pastes redirect URL) end-to-end against the fake;
/// 4b.2 routes the same flow into <c>AudibleClient.ConfigParseExternalLoginResponseAsync</c>.
///
/// Exit codes (per design §10): <c>0</c> success, <c>3</c> auth required/failed,
/// <c>2</c> usage error (caught earlier by <see cref="ParseErrorRewriter"/>).
/// </summary>
internal static class AuthCommand
{
    public const string SchemaResource = "auth-status";

    private static readonly string[] RegionTokens =
    {
        "us", "uk", "de", "fr", "jp", "it", "au", "in", "ca", "es", "br",
    };

    public static Command Create(Func<ParseResult, GlobalOptions> resolveGlobals)
    {
        var cmd = new Command("auth", "Sign in / out of Audible and inspect the active profile.");
        cmd.Subcommands.Add(CreateLogin(resolveGlobals));
        cmd.Subcommands.Add(CreateStatus(resolveGlobals));
        cmd.Subcommands.Add(CreateLogout(resolveGlobals));
        return cmd;
    }

    public static IReadOnlyDictionary<string, object?> ToDictionary(AuthSession session) => new Dictionary<string, object?>
    {
        ["profileAlias"] = session.ProfileAlias,
        ["region"] = session.Region.ToString().ToLowerInvariant(),
        ["accountId"] = session.AccountId,
        ["accountName"] = session.AccountName,
        ["deviceName"] = session.DeviceName,
        ["expiresAt"] = session.ExpiresAt,
        ["isExpired"] = session.IsExpired,
    };

    private static Command CreateLogin(Func<ParseResult, GlobalOptions> resolveGlobals)
    {
        var regionArg = new Argument<string>("region")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = "Audible marketplace: us|uk|de|fr|jp|it|au|in|ca|es|br.",
            DefaultValueFactory = _ => "us",
        };
        regionArg.AcceptOnlyFromAmong(RegionTokens);

        var regionOpt = new Option<string?>("--region")
        {
            Description = "Audible marketplace (alternative to the positional argument).",
        };
        regionOpt.AcceptOnlyFromAmong(RegionTokens);

        var preAmazonOpt = new Option<bool>("--pre-amazon")
        {
            Description = "Use the legacy pre-Amazon Audible username flow (rare).",
        };

        var c = new Command("login", "Sign in to an Audible marketplace.") { regionArg, regionOpt, preAmazonOpt };
        c.SetAction(async (parse, ct) =>
        {
            var globals = resolveGlobals(parse);
            var writer = OutputWriterFactory.Create(ConfigCommand.BuildContext(globals));
            var regionToken = parse.GetValue(regionOpt) ?? parse.GetValue(regionArg) ?? "us";
            var region = ParseRegion(regionToken);
            var preAmazon = parse.GetValue(preAmazonOpt);

            IAuthCallbackBroker broker = CliEnvironment.IsStdinTty
                ? new StdinCallbackBroker(Console.In, CliEnvironment.Error, interactive: true)
                : new NonInteractiveCallbackBroker();

            try
            {
                var svc = CliServiceFactory.AuthServiceFactory();
                var session = await svc.LoginAsync(region, broker, preAmazon, ct).ConfigureAwait(false);
                writer.WriteResource("auth-login-result", ToDictionary(session));
                return 0;
            }
            catch (NonInteractiveCallbackException ex)
            {
                CliEnvironment.Error.WriteLine($"Sign-in needs '{ex.Kind}' input but stdin is not a TTY.");
                CliEnvironment.Error.WriteLine("Re-run from an interactive terminal, or sign in via the Oahu GUI.");
                return 3;
            }
            catch (Exception ex)
            {
                CliEnvironment.Error.WriteLine($"Sign-in failed: {ex.Message}");
                return 3;
            }
        });
        return c;
    }

    private static Command CreateStatus(Func<ParseResult, GlobalOptions> resolveGlobals)
    {
        var c = new Command("status", "List signed-in profiles.");
        c.SetAction(async (parse, ct) =>
        {
            var globals = resolveGlobals(parse);
            var writer = OutputWriterFactory.Create(ConfigCommand.BuildContext(globals));
            var svc = CliServiceFactory.AuthServiceFactory();
            var sessions = await svc.ListSessionsAsync(ct).ConfigureAwait(false);
            var active = await svc.GetActiveAsync(ct).ConfigureAwait(false);

            var rows = new List<IReadOnlyDictionary<string, object?>>(sessions.Count);
            foreach (var s in sessions)
            {
                var d = new Dictionary<string, object?>(ToDictionary(s))
                {
                    ["isActive"] = active is not null && string.Equals(active.ProfileAlias, s.ProfileAlias, StringComparison.Ordinal),
                };
                rows.Add(d);
            }

            writer.WriteCollection(SchemaResource, rows, new[]
            {
                new OutputColumn("profileAlias", "Alias"),
                new OutputColumn("region", "Region"),
                new OutputColumn("accountName", "Account"),
                new OutputColumn("deviceName", "Device"),
                new OutputColumn("isExpired", "Expired"),
                new OutputColumn("isActive", "Active"),
            });

            return sessions.Count == 0 ? 3 : 0;
        });
        return c;
    }

    private static Command CreateLogout(Func<ParseResult, GlobalOptions> resolveGlobals)
    {
        var profileOpt = new Option<string?>("--profile")
        {
            Description = "Profile alias to sign out (defaults to the active profile).",
        };
        var c = new Command("logout", "Remove a signed-in profile.") { profileOpt };
        c.SetAction(async (parse, ct) =>
        {
            var globals = resolveGlobals(parse);
            var writer = OutputWriterFactory.Create(ConfigCommand.BuildContext(globals));
            var svc = CliServiceFactory.AuthServiceFactory();
            var alias = parse.GetValue(profileOpt);
            if (string.IsNullOrWhiteSpace(alias))
            {
                var active = await svc.GetActiveAsync(ct).ConfigureAwait(false);
                if (active is null)
                {
                    CliEnvironment.Error.WriteLine("No active profile. Pass --profile <alias>.");
                    return 3;
                }
                alias = active.ProfileAlias;
            }

            try
            {
                await svc.LogoutAsync(alias!, ct).ConfigureAwait(false);
                writer.WriteSuccess($"Signed out of '{alias}'.");
                return 0;
            }
            catch (Exception ex)
            {
                CliEnvironment.Error.WriteLine($"Sign-out failed: {ex.Message}");
                return 1;
            }
        });
        return c;
    }

    private static CliRegion ParseRegion(string token) => token.ToLowerInvariant() switch
    {
        "us" => CliRegion.Us,
        "uk" => CliRegion.Uk,
        "de" => CliRegion.De,
        "fr" => CliRegion.Fr,
        "jp" => CliRegion.Jp,
        "it" => CliRegion.It,
        "au" => CliRegion.Au,
        "in" => CliRegion.In,
        "ca" => CliRegion.Ca,
        "es" => CliRegion.Es,
        "br" => CliRegion.Br,
        _ => throw new ArgumentException($"Unknown region '{token}'."),
    };
}
