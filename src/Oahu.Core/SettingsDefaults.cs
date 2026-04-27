using System;
using System.IO;

namespace Oahu.Core
{
  /// <summary>
  /// Shared defaults for the user-facing <see cref="DownloadSettings"/> and
  /// <see cref="ExportSettings"/>. Both the Avalonia GUI and the CLI rely on
  /// these so that a freshly created <c>usersettings.json</c> behaves the same
  /// regardless of which surface bootstraps it first.
  ///
  /// <para>
  /// Historically, <c>Oahu.App.Avalonia.UserSettings.Init</c> was the only
  /// place that defaulted <see cref="DownloadSettings.DownloadDirectory"/>.
  /// That meant signing in via <c>oahu-cli</c> on a fresh install left
  /// <c>DownloadDirectory</c> null and the very next download attempt failed
  /// with a <see cref="ArgumentNullException"/> from
  /// <see cref="Directory.CreateDirectory(string)"/>. Centralizing the logic
  /// here removes the asymmetry.
  /// </para>
  /// </summary>
  public static class SettingsDefaults
  {
    /// <summary>
    /// <c>~/Music/Oahu/Downloads</c> on every platform — matches the path the
    /// Avalonia GUI has historically used so the two surfaces share one
    /// download root.
    /// </summary>
    public static string DefaultDownloadDirectory { get; } =
      Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Music",
        "Oahu",
        "Downloads");

    /// <summary>
    /// Apply defaults for any settings that are unset on a freshly loaded
    /// <c>usersettings.json</c>. Idempotent — pre-populated values are
    /// preserved. Both <see cref="Oahu.App.Avalonia.UserSettings.Init"/> (GUI)
    /// and the CLI's <c>OahuUserSettings.Init</c> call this so that downloads
    /// have a sane target directory regardless of which surface registered the
    /// profile.
    /// </summary>
    /// <param name="downloadSettings">
    /// The mutable <see cref="DownloadSettings"/> instance loaded from disk
    /// (or freshly constructed). Required.
    /// </param>
    /// <param name="exportSettings">
    /// The mutable <see cref="ExportSettings"/> instance. Currently no
    /// defaults are forced here — <see cref="ExportSettings.ExportDirectory"/>
    /// remains opt-in until the user enables <c>ExportToAax</c> — but we
    /// thread it through so future defaults can land in one place.
    /// </param>
    public static void ApplyDefaults(
      DownloadSettings downloadSettings,
      ExportSettings exportSettings)
    {
      if (downloadSettings is null)
      {
        throw new ArgumentNullException(nameof(downloadSettings));
      }

      if (string.IsNullOrWhiteSpace(downloadSettings.DownloadDirectory))
      {
        downloadSettings.DownloadDirectory = DefaultDownloadDirectory;
      }

      _ = exportSettings; // reserved for future defaults; keep parameter for symmetry.
    }
  }
}
