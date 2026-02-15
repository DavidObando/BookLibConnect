using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using BookLibConnect.Core.UI.Mac.ViewModels;

namespace BookLibConnect.Core.UI.Mac.Views {
  public partial class SettingsView : UserControl {
    public SettingsView () {
      InitializeComponent ();
    }

    protected override void OnLoaded (RoutedEventArgs e) {
      base.OnLoaded (e);
      if (DataContext is SettingsViewModel vm)
        vm.BrowseFolderRequested += browseFolderAsync;
    }

    protected override void OnUnloaded (RoutedEventArgs e) {
      if (DataContext is SettingsViewModel vm)
        vm.BrowseFolderRequested -= browseFolderAsync;
      base.OnUnloaded (e);
    }

    private async Task<string> browseFolderAsync (string title) {
      var topLevel = TopLevel.GetTopLevel (this);
      if (topLevel is null)
        return null;

      var folders = await topLevel.StorageProvider.OpenFolderPickerAsync (
        new FolderPickerOpenOptions {
          Title = title,
          AllowMultiple = false
        });

      if (folders.Count > 0)
        return folders[0].Path.LocalPath;

      return null;
    }
  }
}
