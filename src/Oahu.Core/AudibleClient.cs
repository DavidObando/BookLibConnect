using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Oahu.Aux;
using Oahu.Aux.Extensions;
using Oahu.CommonTypes;
using Oahu.Core.Ex;
using static Oahu.Aux.Logging;

namespace Oahu.Core
{
  public class AudibleClient
  {
    private IAudibleApi audibleApi;
    private IHardwareIdProvider hardwareIdProvider;

    public AudibleClient(ConfigSettings configSettings, IAuthorizeSettings authSettings, IHardwareIdProvider hardwareIdProvider = null, string dbDir = null)
    {
      Log(3, this);

      this.hardwareIdProvider = hardwareIdProvider;
      ConfigSettings = configSettings;
      if (ConfigSettings is not null)
      {
        ConfigSettings.ChangedSettings += SettingsChangedSettings;
      }

      AuthorizeSettings = authSettings;
      BookLibrary = new BookLibrary(dbDir);
      Authorize = new Authorize(GetConfigurationToken, authSettings);
      AudibleLogin = new AudibleLogin();
    }

    public IProfileKey ProfileKey => Profile?.Key;

    public IProfileAliasKey ProfileAliasKey => Profile?.AliasKey;

    public ConfigSettings ConfigSettings { get; }

    public IBookLibrary BookLibraryExcerpt => BookLibrary;

    public Action WeakConfigEncryptionCallback { set => Authorize.WeakConfigEncryptionCallback = value; }

    public IAudibleApi Api
    {
      get
      {
        if (audibleApi is null)
        {
          if (Profile is null)
          {
            return null;
          }

          audibleApi = new AudibleApi(
            Profile.Profile,
            Authorize.HttpClientAmazon,
            Authorize.HttpClientAudible,
            BookLibrary,
            Authorize.RefreshTokenAsync);
        }

        return audibleApi;
      }
    }

    internal AudibleApi FullApi => audibleApi as AudibleApi;

    private AudibleLogin AudibleLogin { get; }

    private Authorize Authorize { get; }

    private BookLibrary BookLibrary { get; }

    private ProfileBundle Profile { get; set; }

    private IAuthorizeSettings AuthorizeSettings { get; }

    public async Task<RegisterResult> ConfigFromExternalLoginAsync(
      ERegion region,
      bool withPreAmazonUsername,
      Callbacks callbacks)
    {
      Log(3, this, () => $"reg={region}, preAmznAccnt={withPreAmazonUsername}");
      Uri uri = ConfigBuildNewLoginUri(region, withPreAmazonUsername);

      if (callbacks.ExternalLoginCallback is null)
      {
        return null;
      }

      Uri responseUri = callbacks.ExternalLoginCallback(uri);

      var result = await ConfigParseExternalLoginResponseAsync(responseUri, callbacks);

      return result;
    }

    public async Task<RegisterResult> ConfigFromProgrammaticLoginAsync(
      ERegion region,
      bool withPreAmazonUsername,
      Credentials credentials,
      Callbacks callbacks)
    {
      Log(3, this, () => $"reg={region}, preAmznAccnt={withPreAmazonUsername}");

      // Run on thread pool to avoid capturing the UI synchronization context.
      // The synchronous challenge callbacks (CaptchaCallback, MfaCallback, etc.) use
      // Dispatcher.UIThread.Post + TaskCompletionSource.Wait, which deadlocks if
      // the callback fires on the UI thread due to sync context capture.
      return await Task.Run(async () =>
      {
        // Build OAuth URI (this also generates Serial, ClientId, CodeVerifier in AudibleLogin)
        Uri oauthUri = ConfigBuildNewLoginUri(region, withPreAmazonUsername);

        // Perform programmatic login to obtain the redirect URI with authorization code
        var locale = region.FromCountryCode();
        var programmaticLogin = new ProgrammaticLogin();
        Uri responseUri = await programmaticLogin.LoginAsync(
          locale,
          withPreAmazonUsername,
          oauthUri,
          AudibleLogin.Serial,
          credentials,
          callbacks);

        if (responseUri == null)
        {
          Log(1, this, () => "Programmatic login returned no response URI");
          return new RegisterResult(EAuthorizeResult.AuthorizationFailed, null, null);
        }

        // Parse the response and complete device registration (same as external login)
        return await ConfigParseExternalLoginResponseAsync(responseUri, callbacks);
      });
    }

    public Uri ConfigBuildNewLoginUri(
      ERegion region,
      bool withPreAmazonUsername)
    {
      Log(3, this, () => $"reg={region}, preAmznAccnt={withPreAmazonUsername}");
      DisposeProfileAndApi();

      return AudibleLogin.BuildAuthUri(region, withPreAmazonUsername);
    }

    public async Task<RegisterResult> ConfigParseExternalLoginResponseAsync(
      Uri uri,
      Callbacks callbacks)
    {
      using var logGuard = new LogGuard(3, this);

      var profile = AudibleLogin.ParseExternalResponse(uri);

      if (profile is null)
      {
        Log(1, this, () => "response parsing failed.");
        return new RegisterResult(EAuthorizeResult.AuthorizationFailed, null, null);
      }

      var (succ, prevProfile) = await Authorize.RegisterAsync(profile);
      if (!succ)
      {
        return new RegisterResult(EAuthorizeResult.RegistrationFailed, null, null);
      }

      EAuthorizeResult result = EAuthorizeResult.Succ;

      if (profile.Matches(prevProfile))
      {
        Profile = null;
      }

      // TODO modify/test
      // bool deregister = prevProfile is not null &&
      //  (callbacks.DeregisterDeviceConfirmCallback?.Invoke (prevProfile.CreateKeyEx ()) ?? true);

      // if (deregister) {
      //  succ = await Authorize.DeregisterAsync (prevProfile);
      //  if (!succ)
      //    result = EAuthorizeResult.DeregistrationFailed;
      // }
      bool deregister = prevProfile is not null;
      if (deregister)
      {
        result = EAuthorizeResult.DeregistrationFailed;
      }

      return new(result, profile.CreateKeyEx(), prevProfile?.DeviceInfo?.Name);
    }

    public async Task<IProfileAliasKey> ConfigFromFileAsync(
      IProfileAliasKey aliasKey,
      Func<AccountAliasContext, bool> getAccountAliasFunc)
    {
      Log(3, typeof(AudibleClient), () => aliasKey?.ToString());
      DisposeProfileAndApi();
      var resultKey = await FromFileAsync(aliasKey, getAccountAliasFunc);
      return resultKey;
    }

    public IEnumerable<AccountAlias> GetAccountAliases() =>
      BookLibrary.GetAccountAliases();

    public void SetAccountAlias(IProfileKey key, string alias) =>
      BookLibrary.SetAccountAlias(key, alias);

    public async Task<string> GetProfileAliasAsync(
      IProfileKey key, Func<AccountAliasContext, bool> getAccountAliasFunc, bool newAlias)
    {
      var profiles = await Authorize.GetRegisteredProfilesAsync();
      if (profiles is null)
      {
        return null;
      }

      var profile = profiles.FirstOrDefault(p => p.Region == key.Region && string.Equals(p.CustomerInfo.AccountId, key.AccountId));
      if (profile is null)
      {
        return null;
      }

      string alias = profile.GetAccountAlias(BookLibrary, getAccountAliasFunc, newAlias);
      return alias;
    }

    public async Task<IEnumerable<IProfileKeyEx>> GetProfilesAsync()
    {
      Log(3, this);
      var profiles = await Authorize.GetRegisteredProfilesAsync();
      if (profiles is null)
      {
        return null;
      }

      var profileKeys = profiles
        .Select(p => p.CreateKeyEx())
        .ToList();
      return profileKeys;
    }

    public async Task<EAuthorizeResult> RemoveProfileAsync(IProfileKey key)
    {
      Log(3, this, () => key.ToString());
      var result = await Authorize.RemoveProfileAsync(key);
      if (result >= EAuthorizeResult.Succ)
      {
        BookLibrary.RemoveAccountId(key);
        SetProfile(null, null);
      }

      return result;
    }

    public async Task<bool?> ChangeProfileAsync(IProfileKey key, bool aliasChanged)
    {
      Log(3, this, () => key.ToString());

      // Key may be the same but profile could still be different, check Id instead
      bool profileChanged = !Profile.MatchesId(key);
      if (!profileChanged && !aliasChanged)
      {
        return false;
      }

      Log(3, this, () => Authorize?.GetProfile(key)?.CreateAliasKey(BookLibrary, null)?.ToString());
      DisposeProfileAndApi();

      var profiles = await Authorize.GetRegisteredProfilesAsync();
      if (profiles is null)
      {
        return null;
      }

      var profile = profiles.FirstOrDefault(p => p.Matches(key));
      if (profile is null)
      {
        return null;
      }

      SetProfile(profile, null);

      if (profileChanged)
      {
        await Authorize.RefreshTokenAsync(profile, true);
      }

      return true;
    }

    private IProfileAliasKey SetProfile(IProfile profile, Func<AccountAliasContext, bool> getAccountAliasFunc)
    {
      if (profile is null)
      {
        Profile = null;
        return null;
      }

      var key = profile.CreateKey();
      var aliasKey = profile.CreateAliasKey(BookLibrary, getAccountAliasFunc);
      Profile = new ProfileBundle(profile, key, aliasKey);
      return aliasKey;
    }

    private void DisposeProfileAndApi()
    {
      Profile = null;
      audibleApi?.Dispose();
      audibleApi = null;
    }

    private async void SettingsChangedSettings(object sender, EventArgs e) =>
      await Authorize.WriteConfigurationAsync();

    private ConfigurationTokenResult GetConfigurationToken(bool enforce)
    {
      if (!(ConfigSettings?.EncryptConfiguration ?? false) && !enforce)
      {
        return default;
      }

      bool weak = false;
      var sb = new StringBuilder();

      string uid = ApplEnv.UserName.Rot13();
      sb.Append(uid);

      string cid = hardwareIdProvider?.GetCpuId();
      if (cid.IsNullOrWhiteSpace())
      {
        weak = true;
      }
      else
      {
        sb.Append(cid);
      }

      string mbId = hardwareIdProvider?.GetMotherboardId();
      if (mbId.IsNullOrWhiteSpace())
      {
        mbId = hardwareIdProvider?.GetMotherboardPnpDeviceId();
      }

      if (mbId.IsNullOrWhiteSpace())
      {
        weak = true;
      }
      else
      {
        sb.Append(mbId);
      }

      return new(sb.ToString(), weak);
    }

    private async Task<IProfileAliasKey> FromFileAsync(
      IProfileAliasKey aliasKey,
      Func<AccountAliasContext, bool> getAccountAliasFunc)
    {
      var accountAlisases = GetAccountAliases();
      var profiles = await Authorize.GetRegisteredProfilesAsync();
      if (profiles is null)
      {
        return null;
      }

      if (aliasKey is not null)
      {
        profiles = profiles.Where(p => p.Region == aliasKey.Region);
        if (!aliasKey.AccountAlias.IsNullOrWhiteSpace())
        {
          string accountId = accountAlisases.FirstOrDefault(aa => aa.Alias == aliasKey.AccountAlias)?.AccountId;
          if (accountId is not null)
          {
            profiles = profiles.Where(p => string.Equals(p.CustomerInfo.AccountId, accountId));
          }
        }
      }

      if (!profiles.Any())
      {
        return null;
      }

      var profile = profiles.First();

      await Authorize.RefreshTokenAsync(profile, true);

      var resultKey = SetProfile(profile, getAccountAliasFunc);
      return resultKey;
    }
  }
}
