using System.Threading.Tasks;
using Avalonia.Controls;
using Oahu.Core.UI.Avalonia.ViewModels;

namespace Oahu.App.Avalonia
{
  public partial class SetupWizardWindow : Window
  {
    private readonly ProfileWizardViewModel _viewModel;

    public SetupWizardWindow()
    {
      InitializeComponent();
    }

    public SetupWizardWindow(ProfileWizardViewModel viewModel) : this()
    {
      _viewModel = viewModel;
      DataContext = viewModel;
      viewModel.WizardCompleted += (s, e) => Close(viewModel.RegistrationSucceeded);
    }

    /// <summary>
    /// Shows the setup wizard as a modal dialog. Returns true if a profile was created.
    /// </summary>
    public async Task<bool> ShowWizardAsync(Window owner)
    {
      var result = await ShowDialog<bool?>(owner);
      return result == true;
    }
  }
}
