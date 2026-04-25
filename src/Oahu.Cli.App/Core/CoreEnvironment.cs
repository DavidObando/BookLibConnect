using System;
using System.Threading.Tasks;
using Oahu.Aux;
using Oahu.BooksDatabase;
using Oahu.CommonTypes;
using Oahu.Core;
using Oahu.SystemManagement;

namespace Oahu.Cli.App.Core;

/// <summary>
/// Process-wide bootstrap for the Core-backed CLI services. Owns the long-lived
/// <see cref="AudibleClient"/> singleton and the path-sharing override that
/// makes the CLI read/write the same data root as the Avalonia GUI
/// (<c>~/Library/Application Support/Oahu/...</c> on macOS, equivalent on
/// Linux/Windows).
///
/// Lifetime model:
/// <list type="bullet">
///   <item>
///     <description>
///     <see cref="Initialize"/> must run before any code path constructs an
///     <see cref="AudibleClient"/>, opens the books DB, or otherwise touches
///     <see cref="ApplEnv.LocalApplDirectory"/>. It is idempotent.
///     </description>
///   </item>
///   <item>
///     <description>
///     <see cref="InitializeDatabaseAsync"/> applies pending EF Core migrations
///     (idempotent; safe to call before every command).
///     </description>
///   </item>
///   <item>
///     <description>
///     <see cref="Client"/> returns the singleton <see cref="AudibleClient"/>.
///     The CLI process never disposes it; Core's
///     <c>ConfigSettings.ChangedSettings</c> subscription would otherwise leak
///     across re-creations and the process exits soon enough.
///     </description>
///   </item>
/// </list>
///
/// The shared application-data folder name is configurable so tests can route
/// to an isolated temp directory; the production CLI uses
/// <see cref="DefaultSharedApplName"/> to coexist with the Avalonia GUI.
/// </summary>
public static class CoreEnvironment
{
    /// <summary>The Avalonia GUI's <see cref="ApplEnv.ApplName"/>.</summary>
    public const string DefaultSharedApplName = "Oahu";

    private static readonly object Lock = new();
    private static bool initialized;
    private static AudibleClient? client;
    private static OahuUserSettings? settings;
    private static IHardwareIdProvider? hardwareIdProvider;

    /// <summary>
    /// Initialize the shared environment. Routes <see cref="ApplEnv"/> paths to
    /// the GUI-shared root and selects a hardware-id provider for the host OS.
    /// Subsequent calls with the same <paramref name="applName"/> are no-ops;
    /// calls with a different name throw so we do not silently re-route
    /// mid-process.
    /// </summary>
    public static void Initialize(string applName = DefaultSharedApplName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applName);
        lock (Lock)
        {
            if (initialized)
            {
                if (!string.Equals(ApplEnv.ApplName, applName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"CoreEnvironment already initialized with ApplName='{ApplEnv.ApplName}'; cannot re-initialize as '{applName}'.");
                }
                return;
            }

            ApplEnv.OverrideApplName(applName);
            hardwareIdProvider = SelectHardwareIdProvider();
            initialized = true;
        }
    }

    /// <summary>
    /// Initialize the books database (apply pending migrations). Idempotent and
    /// fast when migrations are already applied. Returns <see langword="true"/>
    /// when the database is reachable afterwards.
    /// </summary>
    public static Task<bool> InitializeDatabaseAsync()
    {
        EnsureInitialized();
        return BookDbContextLazyLoad.StartupAsync();
    }

    /// <summary>
    /// Returns the long-lived <see cref="AudibleClient"/> singleton. Lazily
    /// constructs it from the GUI-shared <c>usersettings.json</c> on first
    /// access and runs <see cref="BookDbContextLazyLoad.StartupAsync"/> once
    /// to apply any pending EF Core migrations. Never disposed
    /// (process-lifetime).
    /// </summary>
    public static AudibleClient Client
    {
        get
        {
            EnsureInitialized();
            lock (Lock)
            {
                if (client is not null)
                {
                    return client;
                }

                // One-shot DB migration before we open the BookLibrary used by
                // AudibleClient. .GetAwaiter().GetResult() is safe here: this
                // path is called from the CLI main thread (no sync context) or
                // from tests on the thread pool, never from a UI dispatcher.
                BookDbContextLazyLoad.StartupAsync().GetAwaiter().GetResult();

                settings = SettingsManager.GetUserSettings<OahuUserSettings>();

                // dbDir = null => AudibleClient/BookLibrary/BookDbContext use
                // ApplEnv.LocalApplDirectory derivatives (now routed to the
                // shared "Oahu" root via the OverrideApplName above).
                client = new AudibleClient(
                    settings.ConfigSettings,
                    settings.DownloadSettings,
                    hardwareIdProvider);
                return client;
            }
        }
    }

    /// <summary>
    /// Test hook: drop singletons so the next access reconstructs them. Does
    /// NOT undo <see cref="ApplEnv.OverrideApplName"/> — once set, the path
    /// override persists for the process. Tests that need a different shared
    /// root must orchestrate that via <see cref="Initialize"/> in a fresh
    /// process.
    /// </summary>
    internal static void ResetForTests()
    {
        lock (Lock)
        {
            client = null;
            settings = null;
        }
    }

    /// <summary>
    /// If <see cref="AudibleClient.ProfileKey"/> is null, attempts to load the
    /// "active" profile recorded by the GUI in
    /// <c>UserSettings.DownloadSettings.Profile</c>. Returns <see langword="true"/>
    /// when a profile is loaded after this call. Idempotent — safe to call
    /// before every command that needs <see cref="AudibleClient.Api"/>.
    /// </summary>
    public static async Task<bool> EnsureProfileLoadedAsync()
    {
        var c = Client; // also triggers DB init + settings load
        if (c.ProfileKey is not null)
        {
            return true;
        }

        var aliasKey = settings?.DownloadSettings?.Profile;
        if (aliasKey is null || string.IsNullOrWhiteSpace(aliasKey.AccountAlias))
        {
            return false;
        }

        // The GUI prompts for a new alias when there is none; the CLI is
        // non-interactive in this path, so accept whatever the DB has by
        // returning true (i.e. "use any cached alias").
        var loaded = await c.ConfigFromFileAsync(aliasKey, _ => true).ConfigureAwait(false);
        return loaded is not null;
    }

    private static void EnsureInitialized()
    {
        if (!initialized)
        {
            throw new InvalidOperationException(
                $"{nameof(CoreEnvironment)}.{nameof(Initialize)}() must be called before any other CoreEnvironment member.");
        }
    }

    private static IHardwareIdProvider SelectHardwareIdProvider()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WinHardwareIdProvider();
        }
        if (OperatingSystem.IsMacOS())
        {
            return new MacHardwareIdProvider();
        }
        if (OperatingSystem.IsLinux())
        {
            return new LinuxHardwareIdProvider();
        }
        throw new PlatformNotSupportedException(
            "oahu-cli requires Windows, macOS, or Linux for hardware-id derivation.");
    }
}
