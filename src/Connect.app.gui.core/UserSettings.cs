using System;
using BookLibConnect.Aux;
using BookLibConnect.Core;
using BookLibConnect.Common.Util;

namespace BookLibConnect.App.Gui {

  public class UserSettings : IUserSettings, IInitSettings {
    public UpdateSettings UpdateSettings { get; set; } = new UpdateSettings ();
    public DownloadSettings DownloadSettings { get; set; } = new DownloadSettings ();
    public ConfigSettings ConfigSettings { get; set; } = new ConfigSettings ();
    public ExportSettings ExportSettings { get; set; } = new ExportSettings ();

    public void Init () {
      DownloadSettings.ChangedSettings += onChangedSettings;
      ConfigSettings.ChangedSettings += onChangedSettings;
      ExportSettings.ChangedSettings += onChangedSettings;
    }

    private void onChangedSettings (object sender, EventArgs e) => this.Save ();
  }
}
