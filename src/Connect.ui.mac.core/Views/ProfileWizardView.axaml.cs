using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using core.audiamus.connect.ui.mac.ViewModels;

namespace core.audiamus.connect.ui.mac.Views {
  public partial class ProfileWizardView : UserControl {
    public ProfileWizardView () {
      InitializeComponent ();
    }

    protected override void OnLoaded (RoutedEventArgs e) {
      base.OnLoaded (e);
      // Wire up the clipboard copy via the view since clipboard needs TopLevel access
      if (DataContext is ProfileWizardViewModel vm) {
        vm.CopyLoginUrlCommand.Execute (null);
        // Hook the CopyLoginUrl to actually copy to clipboard
        var btn = this.FindControl<Button> ("btnCopyUrl");
        if (btn is not null) {
          btn.Click += async (s, args) => {
            var topLevel = TopLevel.GetTopLevel (this);
            if (topLevel?.Clipboard is not null && !string.IsNullOrEmpty (vm.LoginUrl))
              await topLevel.Clipboard.SetTextAsync (vm.LoginUrl);
          };
        }
      }
    }
  }
}
