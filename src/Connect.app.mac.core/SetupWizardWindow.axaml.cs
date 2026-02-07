using System.Threading.Tasks;
using Avalonia.Controls;
using core.audiamus.connect.ui.mac.ViewModels;

namespace core.audiamus.connect.app.mac {
  public partial class SetupWizardWindow : Window {
    private readonly ProfileWizardViewModel _viewModel;

    public SetupWizardWindow () {
      InitializeComponent ();
    }

    public SetupWizardWindow (ProfileWizardViewModel viewModel) : this () {
      _viewModel = viewModel;
      DataContext = viewModel;
      viewModel.WizardCompleted += (s, e) => Close (viewModel.RegistrationSucceeded);
    }

    /// <summary>
    /// Shows the setup wizard as a modal dialog. Returns true if a profile was created.
    /// </summary>
    public async Task<bool> ShowWizardAsync (Window owner) {
      var result = await ShowDialog<bool?> (owner);
      return result == true;
    }
  }
}
