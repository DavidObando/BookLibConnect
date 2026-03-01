using System;
using Oahu.Aux;
using Oahu.Core;

namespace Oahu.App.Avalonia
{
  public class UserSettings : IUserSettings, IInitSettings
  {
    public DownloadSettings DownloadSettings { get; set; } = new DownloadSettings();

    public ConfigSettings ConfigSettings { get; set; } = new ConfigSettings();

    public ExportSettings ExportSettings { get; set; } = new ExportSettings();

    public void Init()
    {
      DownloadSettings.ChangedSettings += onChangedSettings;
      ConfigSettings.ChangedSettings += onChangedSettings;
      ExportSettings.ChangedSettings += onChangedSettings;
    }

    private void onChangedSettings(object sender, EventArgs e) => this.Save();
  }
}
