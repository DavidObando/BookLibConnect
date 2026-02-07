using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using core.audiamus.aux;
using core.audiamus.aux.ex;
using core.audiamus.booksdb;
using core.audiamus.connect;
using core.audiamus.connect.ui.mac.ViewModels;
using static core.audiamus.aux.Logging;

namespace core.audiamus.connect.app.mac {
  public partial class MainWindow : Window {

    private readonly MainWindowViewModel _viewModel;
    private readonly UserSettings _userSettings;
    private bool _initDone;

    public MainWindow () {
      InitializeComponent ();
    }

    public MainWindow (MainWindowViewModel viewModel, UserSettings userSettings) : this () {
      _viewModel = viewModel;
      _userSettings = userSettings;
      DataContext = viewModel;
    }

    protected override async void OnOpened (EventArgs e) {
      base.OnOpened (e);
      if (_initDone || _viewModel is null)
        return;

      _initDone = true;
      await initAsync ();
    }

    private async Task initAsync () {
      using var _ = new LogGuard (3, this);

      _viewModel.SetBusy (true, "Initializing...");

      try {
        var client = _viewModel.AudibleClient;

        // Run setup wizard if no profiles exist (mirrors Windows runWizardAsync)
        Log (4, this, () => "before wizard");
        await runWizardAsync (client);

        // Initialize the database (mirrors Windows init)
        Log (4, this, () => "before db");
        _viewModel.SetBusy (true, "Initializing database...");
        await BookDbContextLazyLoad.StartupAsync ();

        // Load profile from config file (mirrors Windows ConfigFromFileAsync)
        Log (4, this, () => "before config");
        _viewModel.SetBusy (true, "Loading configuration...");
        _viewModel.CurrentProfile = await client.ConfigFromFileAsync (
          _userSettings.DownloadSettings?.Profile,
          getAccountAlias
        );

        if (_viewModel.CurrentProfile is not null) {
          _userSettings.DownloadSettings.Profile = new ProfileAliasKey (_viewModel.CurrentProfile);
          _userSettings.Save ();

          // Initialize the API and library (mirrors Windows initLibraryAsync)
          _viewModel.Api = client.Api;
          if (_viewModel.Api is not null) {
            _viewModel.Api.GetAccountAliasFunc = getAccountAlias;

            if (_userSettings.DownloadSettings.AutoUpdateLibrary) {
              _viewModel.SetBusy (true, "Updating library...");
              await _viewModel.Api.GetLibraryAsync (false);

              _viewModel.SetBusy (true, "Downloading cover images...");
              await _viewModel.Api.DownloadCoverImagesAsync ();
            }

            // Load books into the library view
            var books = _viewModel.Api.GetBooks ();
            if (books is not null)
              _viewModel.BookLibrary.LoadBooks (books);
          }
        }

        _viewModel.SetBusy (false, "Ready");
        _viewModel.IsInitialized = true;
        Log (4, this, () => "all done");
      } catch (Exception ex) {
        Log (1, this, () => $"init error: {ex.Message}");
        _viewModel.SetBusy (false, $"Initialization error: {ex.Message}");
      }
    }

    private async Task runWizardAsync (AudibleClient client) {
      using var _ = new LogGuard (3, this);

      var profiles = await client.GetProfilesAsync ();
      bool needsProfile = profiles.IsNullOrEmpty ();

      if (!needsProfile) {
        Log (3, this, () => "profiles exist, skipping wizard");
        return;
      }

      Log (3, this, () => "no profiles found, showing setup wizard");

      var wizardVm = new ProfileWizardViewModel ();
      wizardVm.SetClient (client);

      var wizardWindow = new SetupWizardWindow (wizardVm);
      await wizardWindow.ShowWizardAsync (this);

      if (!wizardVm.RegistrationSucceeded) {
        Log (1, this, () => "wizard: no profile was created");
        _viewModel.StatusMessage = "Warning: No profile was created. You can create one later via Settings.";
      }
    }

    private bool getAccountAlias (AccountAliasContext ctxt) {
      // Auto-accept the alias with customer name for now
      if (ctxt.Alias.IsNullOrWhiteSpace ())
        ctxt.Alias = ctxt.CustomerName;
      return true;
    }
  }
}
