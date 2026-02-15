using System;
using Avalonia;

namespace BookLibConnect.App.Avalonia {
  class Program {
    [STAThread]
    public static void Main (string[] args) => BuildAvaloniaApp ()
      .StartWithClassicDesktopLifetime (args);

    public static AppBuilder BuildAvaloniaApp () =>
      AppBuilder.Configure<App> ()
        .UsePlatformDetect ()
        .WithInterFont ()
        .LogToTrace ();
  }
}
