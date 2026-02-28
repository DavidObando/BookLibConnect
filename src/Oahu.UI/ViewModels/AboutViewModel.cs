using CommunityToolkit.Mvvm.ComponentModel;

namespace Oahu.Core.UI.Avalonia.ViewModels {
  public partial class AboutViewModel : ObservableObject {

    public string AppName => "Oahu";
    public string Version => ThisAssembly.AssemblyFileVersion;
    public string Copyright => $"© {System.DateTime.UtcNow.Year} DavidObando";
    public string Description => "Audible audiobook library manager and converter";
  }
}
