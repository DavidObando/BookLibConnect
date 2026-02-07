using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using core.audiamus.connect;

namespace core.audiamus.connect.ui.mac.ViewModels {
  public partial class SettingsViewModel : ObservableObject {

    private readonly DownloadSettings _downloadSettings;
    private readonly ExportSettings _exportSettings;
    private readonly ConfigSettings _configSettings;

    public SettingsViewModel (DownloadSettings downloadSettings, ExportSettings exportSettings, ConfigSettings configSettings) {
      _downloadSettings = downloadSettings;
      _exportSettings = exportSettings;
      _configSettings = configSettings;
    }

    // Download settings
    public bool AutoUpdateLibrary {
      get => _downloadSettings.AutoUpdateLibrary;
      set {
        _downloadSettings.AutoUpdateLibrary = value;
        OnPropertyChanged ();
        _downloadSettings.OnChange ();
      }
    }

    public bool AutoOpenDownloadDialog {
      get => _downloadSettings.AutoOpenDownloadDialog;
      set {
        _downloadSettings.AutoOpenDownloadDialog = value;
        OnPropertyChanged ();
        _downloadSettings.OnChange ();
      }
    }

    public bool MultiPartDownload {
      get => _downloadSettings.MultiPartDownload;
      set {
        _downloadSettings.MultiPartDownload = value;
        OnPropertyChanged ();
        _downloadSettings.OnChange ();
      }
    }

    public bool KeepEncryptedFiles {
      get => _downloadSettings.KeepEncryptedFiles;
      set {
        _downloadSettings.KeepEncryptedFiles = value;
        OnPropertyChanged ();
        _downloadSettings.OnChange ();
      }
    }

    public string DownloadDirectory {
      get => _downloadSettings.DownloadDirectory;
      set {
        _downloadSettings.DownloadDirectory = value;
        OnPropertyChanged ();
        _downloadSettings.OnChange ();
      }
    }

    // Export settings
    public bool? ExportToAax {
      get => _exportSettings.ExportToAax;
      set {
        _exportSettings.ExportToAax = value;
        OnPropertyChanged ();
      }
    }

    public string ExportDirectory {
      get => _exportSettings.ExportDirectory;
      set {
        _exportSettings.ExportDirectory = value;
        OnPropertyChanged ();
      }
    }

    // Config settings
    public bool EncryptConfiguration {
      get => _configSettings.EncryptConfiguration;
      set {
        _configSettings.EncryptConfiguration = value;
        OnPropertyChanged ();
      }
    }
  }
}
