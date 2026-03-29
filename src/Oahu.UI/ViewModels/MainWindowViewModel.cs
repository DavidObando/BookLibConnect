using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oahu.Core;

namespace Oahu.Core.UI.Avalonia.ViewModels
{
  public partial class MainWindowViewModel : ObservableObject
  {
    [ObservableProperty]
    private string title = "Oahu";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string statusMessage = "Ready";

    [ObservableProperty]
    private object currentView;

    [ObservableProperty]
    private bool isInitialized;

    [ObservableProperty]
    private SettingsViewModel settings;

    [ObservableProperty]
    private bool isSignedIn;

    [ObservableProperty]
    private string profileDisplayName = "Signed out";

    [ObservableProperty]
    private string profileSubtitle = "Sign in to start the setup wizard.";

    [ObservableProperty]
    private string profileInitial = "?";

    [ObservableProperty]
    private Bitmap profileImage;

    [ObservableProperty]
    private bool hasProfileImage;

    public MainWindowViewModel()
    {
      BookLibrary = new BookLibraryViewModel();
      Conversion = new ConversionViewModel();
    }

    public BookLibraryViewModel BookLibrary { get; }

    public ConversionViewModel Conversion { get; }

    public AudibleClient AudibleClient { get; set; }

    public IProfileAliasKey CurrentProfile { get; set; }

    public IAudibleApi Api { get; set; }

    public void SetBusy(bool busy, string message = null)
    {
      IsBusy = busy;
      if (message is not null)
      {
        StatusMessage = message;
      }
    }

    public void InitSettings(DownloadSettings downloadSettings, ExportSettings exportSettings, ConfigSettings configSettings)
    {
      Settings = new SettingsViewModel(downloadSettings, exportSettings, configSettings);
      BookLibrary.SetDownloadSettings(downloadSettings);
    }

    public void SetSignedInProfile(string displayName, string givenName, string subtitle, Bitmap profileImage = null)
    {
      ProfileDisplayName = string.IsNullOrWhiteSpace(displayName) ? "Audible account" : displayName;
      ProfileSubtitle = string.IsNullOrWhiteSpace(subtitle) ? "Audible account" : subtitle;
      ProfileInitial = CreateProfileInitial(givenName, ProfileDisplayName);
      ProfileImage = profileImage;
      IsSignedIn = true;
    }

    public void ClearSignedInProfile()
    {
      CurrentProfile = null;
      Api = null;
      ProfileDisplayName = "Signed out";
      ProfileSubtitle = "Sign in to start the setup wizard.";
      ProfileInitial = "?";
      ProfileImage = null;
      IsSignedIn = false;
    }

    private static string CreateProfileInitial(string givenName, string displayName)
    {
      string source = !string.IsNullOrWhiteSpace(givenName)
        ? givenName
        : displayName;

      if (string.IsNullOrWhiteSpace(source))
      {
        return "?";
      }

      return char.ToUpperInvariant(source.Trim()[0]).ToString();
    }

    partial void OnProfileImageChanged(Bitmap value)
    {
      HasProfileImage = value is not null;
    }
  }
}
