using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oahu.Aux;
using Oahu.Aux.Extensions;
using Oahu.CommonTypes;
using Oahu.Core.UI.Avalonia.Converters;
using static Oahu.Aux.Logging;

namespace Oahu.Core.UI.Avalonia.ViewModels
{
  public partial class ProfileWizardViewModel : ObservableObject
  {
    private AudibleClient client;
    private DownloadSettings downloadSettings;
    private ExportSettings exportSettings;

    [ObservableProperty]
    private int currentStep;

    [ObservableProperty]
    private int totalSteps = 6;

    [ObservableProperty]
    private string stepTitle;

    [ObservableProperty]
    private bool canGoNext;

    [ObservableProperty]
    private bool canGoBack;

    [ObservableProperty]
    private bool isComplete;

    // Step 0: Marketplace selection
    [ObservableProperty]
    private ERegion selectedRegion = ERegion.Us;

    [ObservableProperty]
    private bool usePreAmazonAccount;

    [ObservableProperty]
    private bool preAmazonAllowed;

    // Step 1: Login
    [ObservableProperty]
    private string loginUrl;

    [ObservableProperty]
    private bool isLoggingIn;

    [ObservableProperty]
    private bool loginUrlCopied;

    [ObservableProperty]
    private string pastedResponseUrl;

    [ObservableProperty]
    private bool isProcessingResponse;

    [ObservableProperty]
    private string loginErrorMessage;

    // Step 1: Direct login
    [ObservableProperty]
    private bool useDirectLogin = true;

    [ObservableProperty]
    private string emailInput;

    [ObservableProperty]
    private string passwordInput;

    // Step 1: Challenge handling (CAPTCHA, MFA, CVF, approval)
    [ObservableProperty]
    private bool showChallenge;

    [ObservableProperty]
    private string challengeMessage;

    [ObservableProperty]
    private bool challengeHasImage;

    [ObservableProperty]
    private Bitmap challengeImage;

    [ObservableProperty]
    private bool challengeHasInput;

    [ObservableProperty]
    private string challengeInput;

    [ObservableProperty]
    private string challengeInputWatermark;

    [ObservableProperty]
    private string challengeSubmitText = "Submit";

    private TaskCompletionSource<string> challengeTcs;

    // Step 2: Account alias
    [ObservableProperty]
    private string accountAlias;

    [ObservableProperty]
    private string customerName;

    // Step 3: Download directory
    [ObservableProperty]
    private string downloadDirectory;

    // Step 4: Export to AAX
    [ObservableProperty]
    private bool exportToAax;

    [ObservableProperty]
    private string exportDirectory;

    // Step 5: Completion
    [ObservableProperty]
    private string completionMessage;

    [ObservableProperty]
    private bool registrationSucceeded;

    public ProfileWizardViewModel()
    {
      CurrentStep = 0;
      UpdateStepState();
    }

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

    public static StepVisibilityConverter StepConverter { get; } = new();

    public static OneBasedConverter OneBasedConverter { get; } = new();

    public IReadOnlyList<ERegion> AvailableRegions { get; } =
      Enum.GetValues<ERegion>().ToList().AsReadOnly();

    /// <summary>
    /// The resulting profile key after successful registration.
    /// Set by the wizard upon completion.
    /// </summary>
    public IProfileKeyEx ProfileKey { get; private set; }

    public void SetClient(AudibleClient client)
    {
      this.client = client;
    }

    public void SetSettings(DownloadSettings downloadSettings, ExportSettings exportSettings)
    {
      this.downloadSettings = downloadSettings;
      this.exportSettings = exportSettings;

      string musicDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Music", "Oahu");

      DownloadDirectory = downloadSettings?.DownloadDirectory
        ?? Path.Combine(musicDir, "Downloads");
      ExportToAax = exportSettings?.ExportToAax ?? false;
      ExportDirectory = exportSettings?.ExportDirectory
        ?? Path.Combine(musicDir, "Exports");
    }

    partial void OnSelectedRegionChanged(ERegion value)
    {
      PreAmazonAllowed = value == ERegion.De || value == ERegion.Uk || value == ERegion.Us;
      if (!PreAmazonAllowed)
        UsePreAmazonAccount = false;
    }

    [RelayCommand]
    private void Next()
    {
      if (CurrentStep < TotalSteps - 1)
      {
        // Apply settings before advancing
        ApplyCurrentStepSettings();
        CurrentStep++;
        UpdateStepState();
        if (CurrentStep == 1)
        {
          BuildLoginUrl();
        }

        if (CurrentStep == 5)
        {
          ApplyAllSettings();
        }
      }
    }

    [RelayCommand]
    private void Back()
    {
      if (CurrentStep > 0)
      {
        CurrentStep--;
        UpdateStepState();
      }
    }

    [RelayCommand]
    private void Skip()
    {
      ApplyAllSettings();
      IsComplete = true;
      WizardCompleted?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Finish()
    {
      ApplyAllSettings();
      IsComplete = true;
      WizardCompleted?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenLoginInBrowser()
    {
      if (!LoginUrl.IsNullOrWhiteSpace())
      {
        ShellExecute.File(LoginUrl);
        LoginUrlCopied = true;
        IsLoggingIn = true;
      }
    }

    [RelayCommand]
    private void CopyLoginUrl()
    {
      // Clipboard access will be handled by the view code-behind
      LoginUrlCopied = true;
      IsLoggingIn = true;
    }

    [RelayCommand]
    private async Task BrowseDownloadDirectory()
    {
      if (BrowseDownloadDirectoryRequested is not null)
      {
        string path = await BrowseDownloadDirectoryRequested.Invoke();
        if (!path.IsNullOrWhiteSpace())
        {
          DownloadDirectory = path;
        }
      }
    }

    [RelayCommand]
    private async Task BrowseExportDirectory()
    {
      if (BrowseExportDirectoryRequested is not null)
      {
        string path = await BrowseExportDirectoryRequested.Invoke();
        if (!path.IsNullOrWhiteSpace())
        {
          ExportDirectory = path;
        }
      }
    }

    [RelayCommand]
    private async Task SubmitResponseUrl()
    {
      if (PastedResponseUrl.IsNullOrWhiteSpace())
      {
        return;
      }

      bool succ = Uri.TryCreate(PastedResponseUrl, UriKind.Absolute, out Uri uri);
      if (!succ)
      {
        LoginErrorMessage = "Invalid URL. Please paste the full URL from your browser's address bar.";
        return;
      }

      LoginErrorMessage = null;
      IsProcessingResponse = true;

      try
      {
        var callbacks = new Callbacks
        {
          DeregisterDeviceConfirmCallback = DeregisterDeviceConfirmation,
          GetAccountAliasFunc = GetAccountAliasFromWizard
        };

        var result = await client.ConfigParseExternalLoginResponseAsync(uri, callbacks);

        Log(3, this, () => $"result={result.Result}");

        var key = result.NewProfileKey;
        switch (result.Result)
        {
          case EAuthorizeResult.Succ:
          case EAuthorizeResult.DeregistrationFailed:
            ProfileKey = key;
            CustomerName = key?.AccountName;
            AccountAlias = key?.AccountName;
            RegistrationSucceeded = true;

            // Advance to account alias step
            CurrentStep = 2;
            UpdateStepState();
            if (result.Result == EAuthorizeResult.DeregistrationFailed)
            {
              LoginErrorMessage = $"Note: A previous device \"{result.PrevDeviceName}\" could not be deregistered.";
            }

            break;

          case EAuthorizeResult.AuthorizationFailed:
            LoginErrorMessage = "Authorization failed. The sign-in URL may have expired. Please go back and try again.";
            break;

          case EAuthorizeResult.RegistrationFailed:
            LoginErrorMessage = "Device registration failed. Please try again.";
            break;

          default:
            LoginErrorMessage = $"An error occurred: {result.Result}";
            break;
        }
      }
      catch (Exception ex)
      {
        Log(1, this, () => $"error: {ex.Message}");
        LoginErrorMessage = $"An error occurred: {ex.Message}";
      }
      finally
      {
        IsProcessingResponse = false;
      }
    }

    [RelayCommand]
    private async Task SubmitDirectLogin()
    {
      if (EmailInput.IsNullOrWhiteSpace() || PasswordInput.IsNullOrWhiteSpace())
      {
        LoginErrorMessage = "Please enter both email and password.";
        return;
      }

      LoginErrorMessage = null;
      IsProcessingResponse = true;
      ShowChallenge = false;

      try
      {
        var credentials = new Credentials(EmailInput, PasswordInput);
        var callbacks = new Callbacks
        {
          DeregisterDeviceConfirmCallback = DeregisterDeviceConfirmation,
          GetAccountAliasFunc = GetAccountAliasFromWizard,
          CaptchaCallback = HandleCaptchaCallback,
          MfaCallback = HandleMfaCallback,
          CvfCallback = HandleCvfCallback,
          ApprovalCallback = HandleApprovalCallback,
        };

        var result = await client.ConfigFromProgrammaticLoginAsync(
          SelectedRegion,
          UsePreAmazonAccount,
          credentials,
          callbacks);

        Log(3, this, () => $"result={result.Result}");

        var key = result.NewProfileKey;
        switch (result.Result)
        {
          case EAuthorizeResult.Succ:
          case EAuthorizeResult.DeregistrationFailed:
            ProfileKey = key;
            CustomerName = key?.AccountName;
            AccountAlias = key?.AccountName;
            RegistrationSucceeded = true;

            CurrentStep = 2;
            UpdateStepState();
            if (result.Result == EAuthorizeResult.DeregistrationFailed)
            {
              LoginErrorMessage = $"Note: A previous device \"{result.PrevDeviceName}\" could not be deregistered.";
            }

            break;

          case EAuthorizeResult.AuthorizationFailed:
            LoginErrorMessage = "Authorization failed. Please check your credentials and try again.";
            break;

          case EAuthorizeResult.RegistrationFailed:
            LoginErrorMessage = "Device registration failed. Please try again.";
            break;

          default:
            LoginErrorMessage = $"An error occurred: {result.Result}";
            break;
        }
      }
      catch (OperationCanceledException)
      {
        LoginErrorMessage = "Sign-in was cancelled.";
      }
      catch (TimeoutException ex)
      {
        LoginErrorMessage = $"Connection timed out: {ex.Message}";
      }
      catch (Exception ex)
      {
        Log(1, this, () => $"error: {ex.Message}");
        LoginErrorMessage = $"An error occurred: {ex.Message}";
      }
      finally
      {
        IsProcessingResponse = false;
        ShowChallenge = false;
      }
    }

    [RelayCommand]
    private void SubmitChallenge()
    {
      challengeTcs?.TrySetResult(ChallengeInput ?? string.Empty);
      ShowChallenge = false;
    }

    [RelayCommand]
    private void CancelChallenge()
    {
      challengeTcs?.TrySetResult(null);
      ShowChallenge = false;
    }

    private string HandleCaptchaCallback(byte[] imageData)
    {
      var tcs = new TaskCompletionSource<string>();
      challengeTcs = tcs;
      Dispatcher.UIThread.Post(() =>
      {
        ChallengeImage = new Bitmap(new MemoryStream(imageData));
        ChallengeInput = null;
        ChallengeMessage = "Please enter the text shown in the image:";
        ChallengeHasImage = true;
        ChallengeHasInput = true;
        ChallengeInputWatermark = "Enter CAPTCHA text";
        ChallengeSubmitText = "Submit";
        ShowChallenge = true;
      });
      return tcs.Task.GetAwaiter().GetResult();
    }

    private string HandleMfaCallback()
    {
      var tcs = new TaskCompletionSource<string>();
      challengeTcs = tcs;
      Dispatcher.UIThread.Post(() =>
      {
        ChallengeImage = null;
        ChallengeInput = null;
        ChallengeMessage = "Enter the one-time password (OTP) sent to your device:";
        ChallengeHasImage = false;
        ChallengeHasInput = true;
        ChallengeInputWatermark = "Enter code";
        ChallengeSubmitText = "Submit";
        ShowChallenge = true;
      });
      return tcs.Task.GetAwaiter().GetResult();
    }

    private string HandleCvfCallback()
    {
      var tcs = new TaskCompletionSource<string>();
      challengeTcs = tcs;
      Dispatcher.UIThread.Post(() =>
      {
        ChallengeImage = null;
        ChallengeInput = null;
        ChallengeMessage = "A verification code was sent to your registered email or phone. Enter it below:";
        ChallengeHasImage = false;
        ChallengeHasInput = true;
        ChallengeInputWatermark = "Enter verification code";
        ChallengeSubmitText = "Submit";
        ShowChallenge = true;
      });
      return tcs.Task.GetAwaiter().GetResult();
    }

    private void HandleApprovalCallback()
    {
      var tcs = new TaskCompletionSource<string>();
      challengeTcs = tcs;
      Dispatcher.UIThread.Post(() =>
      {
        ChallengeImage = null;
        ChallengeInput = null;
        ChallengeMessage = "Please approve the sign-in request on your device or email, then click Continue.";
        ChallengeHasImage = false;
        ChallengeHasInput = false;
        ChallengeInputWatermark = null;
        ChallengeSubmitText = "Continue";
        ShowChallenge = true;
      });
      tcs.Task.GetAwaiter().GetResult();
    }

    private void BuildLoginUrl()
    {
      if (client is null)
      {
        return;
      }

      try
      {
        Uri uri = client.ConfigBuildNewLoginUri(SelectedRegion, UsePreAmazonAccount);
        LoginUrl = uri.ToString();
        LoginUrlCopied = false;
        IsLoggingIn = false;
        PastedResponseUrl = null;
        LoginErrorMessage = null;
      }
      catch (Exception ex)
      {
        LoginErrorMessage = ex.Message;
      }
    }

    private void ApplyCurrentStepSettings()
    {
      switch (CurrentStep)
      {
        case 2: // Account alias
          if (client is not null && ProfileKey is not null && !AccountAlias.IsNullOrWhiteSpace())
          {
            client.SetAccountAlias(ProfileKey, AccountAlias);
          }

          break;
        case 3: // Download directory
          if (downloadSettings is not null && !DownloadDirectory.IsNullOrWhiteSpace())
          {
            downloadSettings.DownloadDirectory = DownloadDirectory;
          }

          break;
        case 4: // Export settings
          if (exportSettings is not null)
          {
            exportSettings.ExportToAax = ExportToAax;
            if (ExportToAax && !ExportDirectory.IsNullOrWhiteSpace())
            {
              exportSettings.ExportDirectory = ExportDirectory;
            }
          }

          break;
      }
    }

    private void ApplyAllSettings()
    {
      // Apply account alias
      if (client is not null && ProfileKey is not null && !AccountAlias.IsNullOrWhiteSpace())
      {
        client.SetAccountAlias(ProfileKey, AccountAlias);
      }

      // Apply download directory
      if (downloadSettings is not null && !DownloadDirectory.IsNullOrWhiteSpace())
      {
        downloadSettings.DownloadDirectory = DownloadDirectory;
      }

      // Apply export settings
      if (exportSettings is not null)
      {
        exportSettings.ExportToAax = ExportToAax;
        if (ExportToAax && !ExportDirectory.IsNullOrWhiteSpace())
        {
          exportSettings.ExportDirectory = ExportDirectory;
        }
      }

      // Build completion message
      var key = ProfileKey;
      if (key is not null)
      {
        CompletionMessage = $"Setup complete!\n\n" +
          $"Region: {key.Region}\n" +
          $"Account: {AccountAlias ?? key.AccountName}\n" +
          $"Device: {key.DeviceName}" +
          (!DownloadDirectory.IsNullOrWhiteSpace() ? $"\nDownload folder: {DownloadDirectory}" : "") +
          (ExportToAax ? $"\nExport folder: {ExportDirectory}" : "");
      }
      else
      {
        CompletionMessage = "Setup skipped. You can configure settings later.";
      }
    }

    private void UpdateStepState()
    {
      IsComplete = CurrentStep >= 5;
      CanGoBack = CurrentStep > 0 && CurrentStep < 5;
      CanGoNext = CurrentStep switch
      {
        0 => true,                  // Marketplace → can always proceed
        1 => false,                 // Login → must submit URL to proceed
        2 => RegistrationSucceeded, // Alias → can proceed after login
        3 => true,                  // Download dir → can proceed
        4 => true,                  // Export → can proceed
        _ => false
      };
      StepTitle = CurrentStep switch
      {
        0 => "Select Marketplace",
        1 => "Sign In to Audible",
        2 => "Account Alias",
        3 => "Download Folder",
        4 => "Export Settings",
        5 => "Setup Complete",
        _ => string.Empty
      };
    }

    private bool DeregisterDeviceConfirmation(IProfileKeyEx key) => false;

    private bool GetAccountAliasFromWizard(AccountAliasContext ctxt)
    {
      // Pre-populate from context; the user will edit on step 2
      if (ctxt.Alias.IsNullOrWhiteSpace())
      {
        ctxt.Alias = ctxt.CustomerName;
      }

      CustomerName = ctxt.CustomerName;
      AccountAlias = ctxt.Alias;
      return true;
    }
  }
}
