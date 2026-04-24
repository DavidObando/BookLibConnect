using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Oahu.Cli.App.Paths;

/// <summary>
/// Centralised path layout for oahu-cli.
///
/// Per the design doc (§7), CLI-only state (config + logs) lives under XDG-style
/// per-binary directories. Library cache, profile, queue, and history live alongside
/// the GUI's user-data directory and are owned by <c>Oahu.Core</c> / <c>Oahu.Data</c>
/// (via <c>Oahu.Aux.ApplEnv.LocalApplDirectory</c>) — not here.
/// </summary>
public static class CliPaths
{
    public const string AppName = "oahu";

    /// <summary>
    /// Directory where the user's <c>config.json</c> for the CLI lives.
    /// Linux/macOS: <c>$XDG_CONFIG_HOME/oahu</c> or <c>~/.config/oahu</c>.
    /// Windows:    <c>%APPDATA%\oahu</c>.
    /// </summary>
    public static string ConfigDir { get; } = ResolveConfigDir();

    /// <summary>
    /// Directory where rotated daily log files are written.
    /// Linux/macOS: <c>$XDG_STATE_HOME/oahu/logs</c> or <c>~/.local/state/oahu/logs</c>.
    /// Windows:    <c>%LOCALAPPDATA%\oahu\logs</c>.
    /// </summary>
    public static string LogDir { get; } = ResolveLogDir();

    /// <summary>
    /// Default download directory used by the GUI; the CLI honours the same default.
    /// <c>~/Music/Oahu/Downloads</c> on all platforms.
    /// </summary>
    public static string DefaultDownloadDir { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Music",
            "Oahu",
            "Downloads");

    /// <summary>
    /// User-data directory shared with the Avalonia GUI (<c>Oahu.Aux.ApplEnv.LocalApplDirectory</c>
    /// equivalent — i.e. <c>%LOCALAPPDATA%\oahu</c> on Windows, <c>~/.local/share/oahu</c> on
    /// Linux, <c>~/Library/Application Support/oahu</c> on macOS).
    /// </summary>
    public static string SharedUserDataDir { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppName);

    public static string ConfigFile => Path.Combine(ConfigDir, "config.json");

    public static string TodayLogFile() =>
        Path.Combine(LogDir, $"oahu-cli-{DateTime.Now:yyyyMMdd}.log");

    /// <summary>Ensures every CLI-managed directory exists. Idempotent.</summary>
    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(ConfigDir);
        Directory.CreateDirectory(LogDir);
    }

    private static string ResolveConfigDir()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, AppName);
        }

        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrEmpty(xdg))
        {
            return Path.Combine(xdg, AppName);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config", AppName);
    }

    private static string ResolveLogDir()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, AppName, "logs");
        }

        var xdg = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        if (!string.IsNullOrEmpty(xdg))
        {
            return Path.Combine(xdg, AppName, "logs");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "state", AppName, "logs");
    }
}
