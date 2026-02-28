using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Oahu.Core.UI.Avalonia.ViewModels;

namespace Oahu.Core.UI.Avalonia.Views
{
  public partial class ProfileWizardView : UserControl
  {
    public ProfileWizardView()
    {
      InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
      base.OnLoaded(e);
      if (DataContext is ProfileWizardViewModel vm)
      {
        // Hook clipboard copy for login URL
        var btn = this.FindControl<Button>("btnCopyUrl");
        if (btn is not null)
        {
          btn.Click += async (s, args) =>
          {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard is not null && !string.IsNullOrEmpty(vm.LoginUrl))
              await topLevel.Clipboard.SetTextAsync(vm.LoginUrl);
          };
        }

        // Wire folder picker for download directory
        vm.BrowseDownloadDirectoryRequested += () => browseFolderAsync("Select Download Folder");

        // Wire folder picker for export directory
        vm.BrowseExportDirectoryRequested += () => browseFolderAsync("Select Export Folder");
      }
    }

    private async Task<string> browseFolderAsync(string title)
    {
      var topLevel = TopLevel.GetTopLevel(this);
      if (topLevel is null)
        return null;

      var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
        new FolderPickerOpenOptions
        {
          Title = title,
          AllowMultiple = false
        });

      if (folders.Count > 0)
        return folders[0].Path.LocalPath;

      return null;
    }
  }
}
