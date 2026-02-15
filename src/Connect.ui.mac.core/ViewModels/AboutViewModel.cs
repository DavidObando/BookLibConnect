using CommunityToolkit.Mvvm.ComponentModel;
using BookLibConnect.Aux;

namespace BookLibConnect.Core.UI.Mac.ViewModels {
  public partial class AboutViewModel : ObservableObject {

    public string AppName => "Book Lib Connect";
    public string Version => ThisAssembly.AssemblyFileVersion.ToString();
    public string Copyright => $"© {System.DateTime.UtcNow.Year} DavidObando";
    public string Description => "Audible audiobook library manager and converter for macOS";
  }
}
