using System;
using Oahu.Aux;
using Oahu.Core;

namespace Oahu.Cli.App.Core;

/// <summary>
/// CLI-side mirror of the Avalonia GUI's <c>UserSettings</c> shape. Both classes
/// serialize to/from the same <c>usersettings.json</c> file under the shared
/// application-data root (see <see cref="CoreEnvironment.DefaultSharedApplName"/>),
/// so the CLI honours <see cref="ConfigSettings"/>/<see cref="DownloadSettings"/>/
/// <see cref="ExportSettings"/> changes made through the GUI and vice-versa.
/// </summary>
/// <remarks>
/// We mirror the type rather than reference <c>Oahu.App.Avalonia.UserSettings</c>
/// to avoid pulling Avalonia into the CLI. <see cref="SettingsManager"/>'s
/// JSON-driven hydration only cares about the property shape.
/// </remarks>
public class OahuUserSettings : IUserSettings, IInitSettings
{
    public DownloadSettings DownloadSettings { get; set; } = new DownloadSettings();

    public ConfigSettings ConfigSettings { get; set; } = new ConfigSettings();

    public ExportSettings ExportSettings { get; set; } = new ExportSettings();

    public void Init()
    {
        DownloadSettings.ChangedSettings += OnChangedSettings;
        ConfigSettings.ChangedSettings += OnChangedSettings;
        ExportSettings.ChangedSettings += OnChangedSettings;
    }

    private void OnChangedSettings(object sender, EventArgs e) => this.Save();
}
