using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Oahu.Cli.App.Auth;
using Oahu.Cli.App.Errors;
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
public static class AuthCommand
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

        var usernameOpt = new Option<string?>("--username", "-u")
        {
            Description = "Audible / Amazon account email. Prompted interactively when omitted.",
        };

        var passwordStdinOpt = new Option<bool>("--password-stdin")
        {
            Description = "Read the account password from the first line of stdin (for non-interactive use).",
        };

        var browserOpt = new Option<bool>("--browser")
        {
            Description = "Use the legacy browser-redirect sign-in flow instead of credentials.",
        };

        var noSyncOpt = new Option<bool>("--no-sync")
        {
            Description = "Skip the post-sign-in library sync.",
        };

        var c = new Command("login", "Sign in to an Audible marketplace.")
        {
            regionArg, regionOpt, preAmazonOpt, usernameOpt, passwordStdinOpt, browserOpt, noSyncOpt,
        };
        c.SetAction(async (parse, ct) =>
        {
            var globals = resolveGlobals(parse);
            var writer = OutputWriterFactory.Create(ConfigCommand.BuildContext(globals));
            var regionToken = parse.GetValue(regionOpt) ?? parse.GetValue(regionArg) ?? "us";
            var region = ParseRegion(regionToken);
            var preAmazon = parse.GetValue(preAmazonOpt);
            var browser = parse.GetValue(browserOpt);
            var noSync = parse.GetValue(noSyncOpt);

            IAuthCallbackBroker broker = CliEnvironment.IsStdinTty
                ? new StdinCallbackBroker(Console.In, CliEnvironment.Error, interactive: true)
                : new NonInteractiveCallbackBroker();

            try
            {
                var svc = CliServiceFactory.AuthServiceFactory();

                AuthSession session;
                if (browser)
                {
                    // Legacy browser-based sign-in: build login URI, user pastes
                    // the redirect URL via the broker.
                    session = await svc.LoginAsync(region, broker, preAmazon, ct).ConfigureAwait(false);
                }
                else
                {
                    // Default: programmatic (username + password) sign-in to
                    // mirror the TUI/GUI flow. Any CAPTCHA/MFA/CVF/approval
                    // challenges are routed through the broker.
                    var credentials = ResolveCredentials(parse, usernameOpt, passwordStdinOpt);
                    session = await svc.LoginWithCredentialsAsync(
                        region, broker, credentials, preAmazon, ct).ConfigureAwait(false);
                }

                int? libraryCount = null;
                string? syncWarning = null;
                if (!noSync)
                {
                    try
                    {
                        var library = CliServiceFactory.LibraryServiceFactory();
                        libraryCount = await library.SyncAsync(session.ProfileAlias, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // Sign-in itself succeeded — credentials are stored on
                        // disk via CoreAuthService.CompleteRegistrationAsync.
                        // Report the sync failure but don't fail the command:
                        // the user can re-run `oahu-cli library sync` later.
                        syncWarning = ex.Message;
                        CliEnvironment.Error.WriteLine(
                            $"Sign-in succeeded but library sync failed: {ex.Message}");
                        CliEnvironment.Error.WriteLine(
                            "Run `oahu-cli library sync` to retry.");
                    }
                }

                var dict = new Dictionary<string, object?>(ToDictionary(session))
                {
                    ["librarySynced"] = !noSync && syncWarning is null,
                };
                if (libraryCount is int count)
                {
                    dict["libraryCount"] = count;
                }
                if (syncWarning is not null)
                {
                    dict["syncError"] = syncWarning;
                }
                writer.WriteResource("auth-login-result", dict);
                return ExitCodes.Success;
            }
            catch (NonInteractiveCallbackException ex)
            {
                CliEnvironment.Error.WriteLine($"Sign-in needs '{ex.Kind}' input but stdin is not a TTY.");
                CliEnvironment.Error.WriteLine(
                    "Re-run from an interactive terminal, supply --username and --password-stdin, "
                    + "or sign in via the Oahu TUI/GUI.");
                return ExitCodes.AuthError;
            }
            catch (Exception ex)
            {
                CliEnvironment.Error.WriteLine($"Sign-in failed: {ex.Message}");
                return ExitCodes.AuthError;
            }
        });
        return c;
    }

    private static AuthCredentials ResolveCredentials(
        ParseResult parse,
        Option<string?> usernameOpt,
        Option<bool> passwordStdinOpt)
    {
        var username = parse.GetValue(usernameOpt);
        var passwordFromStdin = parse.GetValue(passwordStdinOpt);

        if (string.IsNullOrWhiteSpace(username))
        {
            if (!CliEnvironment.IsStdinTty)
            {
                throw new NonInteractiveCallbackException("username");
            }
            CliEnvironment.Error.Write("Audible / Amazon email: ");
            username = Console.In.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(username))
            {
                throw new InvalidOperationException("Email is required.");
            }
        }

        string password;
        if (passwordFromStdin)
        {
            // Read exactly one line; works for `echo $PW | oahu-cli auth login --password-stdin`.
            password = Console.In.ReadLine() ?? string.Empty;
        }
        else if (CliEnvironment.IsStdinTty)
        {
            CliEnvironment.Error.Write("Password: ");
            password = ReadMaskedPassword();
            CliEnvironment.Error.WriteLine();
        }
        else
        {
            throw new NonInteractiveCallbackException("password");
        }

        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException("Password is required.");
        }

        return new AuthCredentials(username!, password);
    }

    private static string ReadMaskedPassword()
    {
        var sb = new StringBuilder();
        while (true)
        {
            ConsoleKeyInfo key;
            try
            {
                key = Console.ReadKey(intercept: true);
            }
            catch (InvalidOperationException)
            {
                // Stdin redirected after IsStdinTty check; fall back to ReadLine.
                return Console.In.ReadLine() ?? string.Empty;
            }

            if (key.Key == ConsoleKey.Enter)
            {
                return sb.ToString();
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (sb.Length > 0)
                {
                    sb.Length--;
                }
                continue;
            }
            if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
            {
                sb.Append(key.KeyChar);
            }
        }
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

            return sessions.Count == 0 ? ExitCodes.AuthError : ExitCodes.Success;
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
                    return ExitCodes.AuthError;
                }
                alias = active.ProfileAlias;
            }

            if (globals.DryRun)
            {
                writer.WriteResource("auth-logout-plan", new Dictionary<string, object?>
                {
                    ["wouldLogout"] = alias,
                });
                return ExitCodes.Success;
            }

            try
            {
                await svc.LogoutAsync(alias!, ct).ConfigureAwait(false);
                writer.WriteSuccess($"Signed out of '{alias}'.");
                return ExitCodes.Success;
            }
            catch (Exception ex)
            {
                CliEnvironment.Error.WriteLine($"Sign-out failed: {ex.Message}");
                return ExitCodes.GenericFailure;
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
