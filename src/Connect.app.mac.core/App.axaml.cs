using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BookLibConnect.Aux;
using BookLibConnect.Core;
using BookLibConnect.Core.UI.Mac.ViewModels;
using BookLibConnect.SystemManagement.Mac;
using static BookLibConnect.Aux.Logging;

namespace BookLibConnect.App.Mac {
  public partial class App : Application {
    public override void Initialize () {
      AvaloniaXamlLoader.Load (this);
    }

    public override void OnFrameworkInitializationCompleted () {
      if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {

        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

        Log (1, this, () =>
          $"{ApplEnv.ApplName} {ApplEnv.AssemblyVersion} on macOS");

        Logging.Level = 3;
        Logging.InstantFlush = true;

        var userSettings = SettingsManager.GetUserSettings<UserSettings> ();

        var hardwareIdProvider = new MacHardwareIdProvider ();
        var audibleClient = new AudibleClient (
          userSettings.ConfigSettings,
          userSettings.DownloadSettings,
          hardwareIdProvider
        );

        var viewModel = new MainWindowViewModel ();
        viewModel.AudibleClient = audibleClient;
        viewModel.InitSettings (
          userSettings.DownloadSettings,
          userSettings.ExportSettings,
          userSettings.ConfigSettings
        );
        viewModel.Title = ApplEnv.AssemblyTitle ?? "Book Lib Connect";

        var mainWindow = new MainWindow (viewModel, userSettings);
        desktop.MainWindow = mainWindow;
      }

      base.OnFrameworkInitializationCompleted ();
    }
  }
}
