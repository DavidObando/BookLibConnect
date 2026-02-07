using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using core.audiamus.aux;
using core.audiamus.aux.ex;
using core.audiamus.common;
using core.audiamus.connect.ui.mac.Converters;
using static core.audiamus.aux.Logging;

namespace core.audiamus.connect.ui.mac.ViewModels {
  public partial class ProfileWizardViewModel : ObservableObject {

    public static StepVisibilityConverter StepConverter { get; } = new ();
    public static OneBasedConverter OneBasedConverter { get; } = new ();

    private AudibleClient _client;

    [ObservableProperty]
    private int _currentStep;

    [ObservableProperty]
    private int _totalSteps = 4;

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

    // Step 3: Completion
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

    public ProfileWizardViewModel () {
      CurrentStep = 0;
      UpdateStepState ();
    }

    public void SetClient (AudibleClient client) {
      _client = client;
    }

    partial void OnSelectedRegionChanged (ERegion value) {
      PreAmazonAllowed = value == ERegion.de || value == ERegion.uk || value == ERegion.us;
      if (!PreAmazonAllowed)
        UsePreAmazonAccount = false;
    }

    [RelayCommand]
    private void Next () {
      if (CurrentStep < TotalSteps - 1) {
        CurrentStep++;
        UpdateStepState ();
        if (CurrentStep == 1)
          buildLoginUrl ();
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
      IsComplete = true;
      WizardCompleted?.Invoke (this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Finish () {
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
          GetAccountAliasFunc = getAccountAlias
        };

        var result = await _client.ConfigParseExternalLoginResponseAsync (uri, callbacks);

        Log (3, this, () => $"result={result.Result}");

        var key = result.NewProfileKey;
        switch (result.Result) {
          case EAuthorizeResult.succ:
            ProfileKey = key;
            CompletionMessage = $"Profile created successfully!\n\n" +
              $"Region: {key?.Region}\n" +
              $"Account: {key?.AccountName}\n" +
              $"Device: {key?.DeviceName}";
            RegistrationSucceeded = true;
            CurrentStep = 3;
            UpdateStepState ();
            break;

          case EAuthorizeResult.deregistrationFailed:
            ProfileKey = key;
            CompletionMessage = $"Profile created successfully!\n\n" +
              $"Region: {key?.Region}\n" +
              $"Account: {key?.AccountName}\n" +
              $"Device: {key?.DeviceName}\n\n" +
              $"Note: A previous device registration \"{result.PrevDeviceName}\" could not be removed.";
            RegistrationSucceeded = true;
            CurrentStep = 3;
            UpdateStepState ();
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

    private void UpdateStepState () {
      CanGoBack = CurrentStep > 0 && CurrentStep < 3;
      CanGoNext = CurrentStep < 1; // Only allow Next from step 0 to step 1
      StepTitle = CurrentStep switch {
        0 => "Select Marketplace",
        1 => "Sign In to Audible",
        2 => "Account Alias",
        3 => "Complete",
        _ => string.Empty
      };
    }

    private bool deregisterDeviceConfirmation (IProfileKeyEx key) => false;

    private bool getAccountAlias (AccountAliasContext ctxt) {
      // Auto-accept the alias context; user can rename later
      // Use the customer name as the initial alias
      if (ctxt.Alias.IsNullOrWhiteSpace ())
        ctxt.Alias = ctxt.CustomerName;
      return true;
    }
  }
}
