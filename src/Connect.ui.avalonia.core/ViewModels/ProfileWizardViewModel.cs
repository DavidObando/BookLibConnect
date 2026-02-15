using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BookLibConnect.Aux;
using BookLibConnect.Aux.Extensions;
using BookLibConnect.CommonTypes;
using BookLibConnect.Core.UI.Avalonia.Converters;
using static BookLibConnect.Aux.Logging;

namespace BookLibConnect.Core.UI.Avalonia.ViewModels {
  public partial class ProfileWizardViewModel : ObservableObject {

    public static StepVisibilityConverter StepConverter { get; } = new ();
    public static OneBasedConverter OneBasedConverter { get; } = new ();

    private AudibleClient _client;
    private DownloadSettings _downloadSettings;
    private ExportSettings _exportSettings;

    [ObservableProperty]
    private int _currentStep;

    [ObservableProperty]
    private int _totalSteps = 6;

    [ObservableProperty]
    private string _stepTitle;

    [ObservableProperty]
    private bool _canGoNext;

    [ObservableProperty]
    private bool _canGoBack;

    [ObservableProperty]
    private bool _isComplete;

    // Step 0: Marketplace selection
    [ObservableProperty]
    private ERegion _selectedRegion = ERegion.us;

    public IReadOnlyList<ERegion> AvailableRegions { get; } =
      Enum.GetValues<ERegion> ().ToList ().AsReadOnly ();

    [ObservableProperty]
    private bool _usePreAmazonAccount;

    [ObservableProperty]
    private bool _preAmazonAllowed;

    // Step 1: Login
    [ObservableProperty]
    private string _loginUrl;

    [ObservableProperty]
    private bool _isLoggingIn;

    [ObservableProperty]
    private bool _loginUrlCopied;

    [ObservableProperty]
    private string _pastedResponseUrl;

    [ObservableProperty]
    private bool _isProcessingResponse;

    [ObservableProperty]
    private string _loginErrorMessage;

    // Step 2: Account alias
    [ObservableProperty]
    private string _accountAlias;

    [ObservableProperty]
    private string _customerName;

    // Step 3: Download directory
    [ObservableProperty]
    private string _downloadDirectory;

    // Step 4: Export to AAX
    [ObservableProperty]
    private bool _exportToAax;

    [ObservableProperty]
    private string _exportDirectory;

    // Step 5: Completion
    [ObservableProperty]
    private string _completionMessage;

    [ObservableProperty]
    private bool _registrationSucceeded;

    /// <summary>
    /// The resulting profile key after successful registration. 
    /// Set by the wizard upon completion.
    /// </summary>
    public IProfileKeyEx ProfileKey { get; private set; }

    /// <summary>
    /// Event raised when the wizard completes (success or skip).
    /// </summary>
    public event EventHandler WizardCompleted;

    /// <summary>
    /// Event raised when the user wants to browse for a download directory.
    /// The view code-behind handles the folder picker and sets the result.
    /// </summary>
    public event Func<Task<string>> BrowseDownloadDirectoryRequested;

    /// <summary>
    /// Event raised when the user wants to browse for an export directory.
    /// The view code-behind handles the folder picker and sets the result.
    /// </summary>
    public event Func<Task<string>> BrowseExportDirectoryRequested;

    public ProfileWizardViewModel () {
      CurrentStep = 0;
      UpdateStepState ();
    }

    public void SetClient (AudibleClient client) {
      _client = client;
    }

    public void SetSettings (DownloadSettings downloadSettings, ExportSettings exportSettings) {
      _downloadSettings = downloadSettings;
      _exportSettings = exportSettings;

      string musicDir = Path.Combine (
        Environment.GetFolderPath (Environment.SpecialFolder.UserProfile), "Music", "BookLibConnect");

      DownloadDirectory = downloadSettings?.DownloadDirectory
        ?? Path.Combine (musicDir, "Downloads");
      ExportToAax = exportSettings?.ExportToAax ?? false;
      ExportDirectory = exportSettings?.ExportDirectory
        ?? Path.Combine (musicDir, "Exports");
    }

    partial void OnSelectedRegionChanged (ERegion value) {
      PreAmazonAllowed = value == ERegion.de || value == ERegion.uk || value == ERegion.us;
      if (!PreAmazonAllowed)
        UsePreAmazonAccount = false;
    }

    [RelayCommand]
    private void Next () {
      if (CurrentStep < TotalSteps - 1) {
        // Apply settings before advancing
        applyCurrentStepSettings ();
        CurrentStep++;
        UpdateStepState ();
        if (CurrentStep == 1)
          buildLoginUrl ();
        if (CurrentStep == 5)
          applyAllSettings ();
      }
    }

    [RelayCommand]
    private void Back () {
      if (CurrentStep > 0) {
        CurrentStep--;
        UpdateStepState ();
      }
    }

    [RelayCommand]
    private void Skip () {
      applyAllSettings ();
      IsComplete = true;
      WizardCompleted?.Invoke (this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Finish () {
      applyAllSettings ();
      IsComplete = true;
      WizardCompleted?.Invoke (this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenLoginInBrowser () {
      if (!LoginUrl.IsNullOrWhiteSpace ()) {
        ShellExecute.File (LoginUrl);
        LoginUrlCopied = true;
        IsLoggingIn = true;
      }
    }

    [RelayCommand]
    private void CopyLoginUrl () {
      // Clipboard access will be handled by the view code-behind
      LoginUrlCopied = true;
      IsLoggingIn = true;
    }

    [RelayCommand]
    private async Task BrowseDownloadDirectory () {
      if (BrowseDownloadDirectoryRequested is not null) {
        string path = await BrowseDownloadDirectoryRequested.Invoke ();
        if (!path.IsNullOrWhiteSpace ())
          DownloadDirectory = path;
      }
    }

    [RelayCommand]
    private async Task BrowseExportDirectory () {
      if (BrowseExportDirectoryRequested is not null) {
        string path = await BrowseExportDirectoryRequested.Invoke ();
        if (!path.IsNullOrWhiteSpace ())
          ExportDirectory = path;
      }
    }

    [RelayCommand]
    private async Task SubmitResponseUrl () {
      if (PastedResponseUrl.IsNullOrWhiteSpace ())
        return;

      bool succ = Uri.TryCreate (PastedResponseUrl, UriKind.Absolute, out Uri uri);
      if (!succ) {
        LoginErrorMessage = "Invalid URL. Please paste the full URL from your browser's address bar.";
        return;
      }

      LoginErrorMessage = null;
      IsProcessingResponse = true;

      try {
        var callbacks = new Callbacks {
          DeregisterDeviceConfirmCallback = deregisterDeviceConfirmation,
          GetAccountAliasFunc = getAccountAliasFromWizard
        };

        var result = await _client.ConfigParseExternalLoginResponseAsync (uri, callbacks);

        Log (3, this, () => $"result={result.Result}");

        var key = result.NewProfileKey;
        switch (result.Result) {
          case EAuthorizeResult.succ:
          case EAuthorizeResult.deregistrationFailed:
            ProfileKey = key;
            CustomerName = key?.AccountName;
            AccountAlias = key?.AccountName;
            RegistrationSucceeded = true;
            // Advance to account alias step
            CurrentStep = 2;
            UpdateStepState ();
            if (result.Result == EAuthorizeResult.deregistrationFailed)
              LoginErrorMessage = $"Note: A previous device \"{result.PrevDeviceName}\" could not be deregistered.";
            break;

          case EAuthorizeResult.authorizationFailed:
            LoginErrorMessage = "Authorization failed. The sign-in URL may have expired. Please go back and try again.";
            break;

          case EAuthorizeResult.registrationFailed:
            LoginErrorMessage = "Device registration failed. Please try again.";
            break;

          default:
            LoginErrorMessage = $"An error occurred: {result.Result}";
            break;
        }
      } catch (Exception ex) {
        Log (1, this, () => $"error: {ex.Message}");
        LoginErrorMessage = $"An error occurred: {ex.Message}";
      } finally {
        IsProcessingResponse = false;
      }
    }

    private void buildLoginUrl () {
      if (_client is null)
        return;
      try {
        Uri uri = _client.ConfigBuildNewLoginUri (SelectedRegion, UsePreAmazonAccount);
        LoginUrl = uri.ToString ();
        LoginUrlCopied = false;
        IsLoggingIn = false;
        PastedResponseUrl = null;
        LoginErrorMessage = null;
      } catch (Exception ex) {
        LoginErrorMessage = ex.Message;
      }
    }

    private void applyCurrentStepSettings () {
      switch (CurrentStep) {
        case 2: // Account alias
          if (_client is not null && ProfileKey is not null && !AccountAlias.IsNullOrWhiteSpace ())
            _client.SetAccountAlias (ProfileKey, AccountAlias);
          break;
        case 3: // Download directory
          if (_downloadSettings is not null && !DownloadDirectory.IsNullOrWhiteSpace ())
            _downloadSettings.DownloadDirectory = DownloadDirectory;
          break;
        case 4: // Export settings
          if (_exportSettings is not null) {
            _exportSettings.ExportToAax = ExportToAax;
            if (ExportToAax && !ExportDirectory.IsNullOrWhiteSpace ())
              _exportSettings.ExportDirectory = ExportDirectory;
          }
          break;
      }
    }

    private void applyAllSettings () {
      // Apply account alias
      if (_client is not null && ProfileKey is not null && !AccountAlias.IsNullOrWhiteSpace ())
        _client.SetAccountAlias (ProfileKey, AccountAlias);

      // Apply download directory
      if (_downloadSettings is not null && !DownloadDirectory.IsNullOrWhiteSpace ())
        _downloadSettings.DownloadDirectory = DownloadDirectory;

      // Apply export settings
      if (_exportSettings is not null) {
        _exportSettings.ExportToAax = ExportToAax;
        if (ExportToAax && !ExportDirectory.IsNullOrWhiteSpace ())
          _exportSettings.ExportDirectory = ExportDirectory;
      }

      // Build completion message
      var key = ProfileKey;
      if (key is not null) {
        CompletionMessage = $"Setup complete!\n\n" +
          $"Region: {key.Region}\n" +
          $"Account: {AccountAlias ?? key.AccountName}\n" +
          $"Device: {key.DeviceName}" +
          (!DownloadDirectory.IsNullOrWhiteSpace () ? $"\nDownload folder: {DownloadDirectory}" : "") +
          (ExportToAax ? $"\nExport folder: {ExportDirectory}" : "");
      } else {
        CompletionMessage = "Setup skipped. You can configure settings later.";
      }
    }

    private void UpdateStepState () {
      IsComplete = CurrentStep >= 5;
      CanGoBack = CurrentStep > 0 && CurrentStep < 5;
      CanGoNext = CurrentStep switch {
        0 => true,                  // Marketplace → can always proceed
        1 => false,                 // Login → must submit URL to proceed
        2 => RegistrationSucceeded, // Alias → can proceed after login
        3 => true,                  // Download dir → can proceed
        4 => true,                  // Export → can proceed
        _ => false
      };
      StepTitle = CurrentStep switch {
        0 => "Select Marketplace",
        1 => "Sign In to Audible",
        2 => "Account Alias",
        3 => "Download Folder",
        4 => "Export Settings",
        5 => "Setup Complete",
        _ => string.Empty
      };
    }

    private bool deregisterDeviceConfirmation (IProfileKeyEx key) => false;

    private bool getAccountAliasFromWizard (AccountAliasContext ctxt) {
      // Pre-populate from context; the user will edit on step 2
      if (ctxt.Alias.IsNullOrWhiteSpace ())
        ctxt.Alias = ctxt.CustomerName;
      CustomerName = ctxt.CustomerName;
      AccountAlias = ctxt.Alias;
      return true;
    }
  }
}
