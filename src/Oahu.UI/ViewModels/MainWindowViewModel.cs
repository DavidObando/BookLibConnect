using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oahu.Core;

namespace Oahu.Core.UI.Avalonia.ViewModels {
  public partial class MainWindowViewModel : ObservableObject {

    [ObservableProperty]
    private string _title = "Oahu";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private object _currentView;

    [ObservableProperty]
    private bool _isInitialized;

    [ObservableProperty]
    private SettingsViewModel _settings;

    public BookLibraryViewModel BookLibrary { get; }
    public ConversionViewModel Conversion { get; }

    public AudibleClient AudibleClient { get; set; }
    public IProfileAliasKey CurrentProfile { get; set; }
    public IAudibleApi Api { get; set; }

    public MainWindowViewModel () {
      BookLibrary = new BookLibraryViewModel ();
      Conversion = new ConversionViewModel ();
    }

    public void SetBusy (bool busy, string message = null) {
      IsBusy = busy;
      if (message is not null)
        StatusMessage = message;
    }

    public void InitSettings (DownloadSettings downloadSettings, ExportSettings exportSettings, ConfigSettings configSettings) {
      Settings = new SettingsViewModel (downloadSettings, exportSettings, configSettings);
    }
  }
}
