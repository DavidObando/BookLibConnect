using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BookLibConnect.Aux;
using BookLibConnect.Core;
using BookLibConnect.Core.UI.Avalonia.ViewModels;
using BookLibConnect.CommonTypes;
using BookLibConnect.SystemManagement;
using BookLibConnect.SystemManagement.Linux;
using BookLibConnect.SystemManagement.Mac;
using static BookLibConnect.Aux.Logging;

namespace BookLibConnect.App.Avalonia {
  public partial class App : Application {
    public override void Initialize () {
      AvaloniaXamlLoader.Load (this);
    }

    public override void OnFrameworkInitializationCompleted () {
      if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {

        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

        Log (1, this, () =>
          $"{ApplEnv.ApplName} {ApplEnv.AssemblyVersion}");

        Logging.Level = 3;
        Logging.InstantFlush = true;

        var userSettings = SettingsManager.GetUserSettings<UserSettings> ();

        var hardwareIdProvider = getHardwareIdProvider ();
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

    private static IHardwareIdProvider getHardwareIdProvider () {
      if (OperatingSystem.IsWindows ())
        return new WinHardwareIdProvider ();
      if (OperatingSystem.IsMacOS ())
        return new MacHardwareIdProvider ();
      if (OperatingSystem.IsLinux ())
        return new LinuxHardwareIdProvider ();
      throw new PlatformNotSupportedException ();
    }
  }
}
