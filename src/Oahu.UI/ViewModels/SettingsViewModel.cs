using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oahu.Aux.Extensions;
using Oahu.Core;

namespace Oahu.Core.UI.Avalonia.ViewModels
{
  public partial class SettingsViewModel : ObservableObject
  {
    private readonly DownloadSettings _downloadSettings;
    private readonly ExportSettings _exportSettings;
    private readonly ConfigSettings _configSettings;

    public SettingsViewModel(DownloadSettings downloadSettings, ExportSettings exportSettings, ConfigSettings configSettings)
    {
      _downloadSettings = downloadSettings;
      _exportSettings = exportSettings;
      _configSettings = configSettings;
    }

    /// <summary>
    /// Event raised when the user wants to browse for a folder.
    /// The view code-behind handles the native folder picker and returns the selected path.
    /// </summary>
    public event Func<string, Task<string>> BrowseFolderRequested;

    // Download settings
    public bool AutoUpdateLibrary
    {
      get => _downloadSettings.AutoUpdateLibrary;
      set
      {
        _downloadSettings.AutoUpdateLibrary = value;
        OnPropertyChanged();
        _downloadSettings.OnChange();
      }
    }

    public bool MultiPartDownload
    {
      get => _downloadSettings.MultiPartDownload;
      set
      {
        _downloadSettings.MultiPartDownload = value;
        OnPropertyChanged();
        _downloadSettings.OnChange();
      }
    }

    public bool KeepEncryptedFiles
    {
      get => _downloadSettings.KeepEncryptedFiles;
      set
      {
        _downloadSettings.KeepEncryptedFiles = value;
        OnPropertyChanged();
        _downloadSettings.OnChange();
      }
    }

    public string DownloadDirectory
    {
      get => _downloadSettings.DownloadDirectory;
      set
      {
        _downloadSettings.DownloadDirectory = value;
        OnPropertyChanged();
        _downloadSettings.OnChange();
      }
    }

    // Export settings
    public bool? ExportToAax
    {
      get => _exportSettings.ExportToAax;
      set
      {
        _exportSettings.ExportToAax = value;
        OnPropertyChanged();
        _exportSettings.OnChange();
      }
    }

    public string ExportDirectory
    {
      get => _exportSettings.ExportDirectory;
      set
      {
        _exportSettings.ExportDirectory = value;
        OnPropertyChanged();
        _exportSettings.OnChange();
      }
    }

    // Config settings
    public bool EncryptConfiguration
    {
      get => _configSettings.EncryptConfiguration;
      set
      {
        _configSettings.EncryptConfiguration = value;
        OnPropertyChanged();
        _configSettings.OnChange();
      }
    }

    [RelayCommand]
    private async Task BrowseDownloadDirectory()
    {
      if (BrowseFolderRequested is not null)
      {
        string path = await BrowseFolderRequested.Invoke("Select Download Folder");
        if (!path.IsNullOrWhiteSpace())
        {
          DownloadDirectory = path;
        }
      }
    }

    [RelayCommand]
    private async Task BrowseExportDirectory()
    {
      if (BrowseFolderRequested is not null)
      {
        string path = await BrowseFolderRequested.Invoke("Select Export Folder");
        if (!path.IsNullOrWhiteSpace())
        {
          ExportDirectory = path;
        }
      }
    }
  }
}
