using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Oahu.Aux;
using Oahu.Aux.Extensions;
using Oahu.Core.Ex;
using static Oahu.Aux.Logging;

namespace Oahu.Core
{
  class Authorize
  {
    internal const string OsVersion = @"google/sdk_gphone64_x86_64/emu64xa:14/UPB5.230623.003/10615560:userdebug/dev-keys";
    internal const string OsVersionNumber = "34";
    internal const string AppVersion = "2090253826";
    internal const string AppVersionName = "25.38.26";
    internal const string SoftwareVersion = "130050002";
    internal const string AppName = "com.audible.application";
    internal const string DeviceModel = "sdk_gphone64_x86_64";
    internal const string OsFamily = "android";
    internal const string Manufacturer = "Google";
    internal const string DeviceProduct = "sdk_phone64_x86_64";

    private const string HttpAuthorityAmzn = @"https://api.amazon.";
    private const string HttpAuthorityAdbl = @"https://api.audible.";
    private const string HttpPathRegister = @"/auth/register";
    private const string HttpPathDeregister = @"/auth/deregister";
    private const string HttpPathToken = @"/auth/token";

    public Authorize(ConfigTokenDelegate getTokenFunc, IAuthorizeSettings settings)
    {
      Log(3, this);
      GetTokenFunc = getTokenFunc;
      Settings = settings;
    }

    public HttpClientEx HttpClientAmazon { get; private set; }

    public HttpClientEx HttpClientAudible { get; private set; }

    public Action WeakConfigEncryptionCallback { private get; set; }

    private IAuthorizeSettings Settings { get; }

    // private Uri BaseUri => HttpClientAmazon?.BaseAddress;
    private Configuration Configuration { get; set; }

    private ConfigTokenDelegate GetTokenFunc { get; }

    public async Task<(bool, IProfile)> RegisterAsync(Profile profile)
    {
      using var logGuard = new LogGuard(3, this);

      EnsureHttpClient(profile);

      try
      {
        var request = BuildRegisterRequest(profile);

        var http = HttpClient(profile);

        await request.LogAsync(4, this, http.DefaultRequestHeaders, http.CookieContainer, http.BaseAddress);

        var response = await http.SendAsync(request);
        await response.LogAsync(4, this, http.CookieContainer, http.BaseAddress);

        response.EnsureSuccessStatusCode();
        string content = await response.Content.ReadAsStringAsync();

        return await AddProfileToConfig(profile, content);
      }
      catch (Exception exc)
      {
        Log(1, this, () => exc.Summary());
        return (false, null);
      }
    }

    public async Task<bool> DeregisterAsync(IProfile profile)
    {
      EnsureHttpClient(profile);
      try
      {
        await RefreshTokenAsync(profile);

        var request = BuildDeregisterRequest(profile);

        var http = HttpClient(profile);

        await request.LogAsync(4, this, http.DefaultRequestHeaders, http.CookieContainer, http.BaseAddress);

        var response = await http.SendAsync(request);
        await response.LogAsync(4, this, http.CookieContainer, http.BaseAddress);

        response.EnsureSuccessStatusCode();
        string content = await response.Content.ReadAsStringAsync();

        return true;
      }
      catch (Exception exc)
      {
        Log(1, this, () => exc.Summary());
        return false;
      }
    }

    public async Task<EAuthorizeResult> RemoveProfileAsync(IProfileKey key)
    {
      Log(3, this, () => key.ToString());

      IProfile profile = Configuration.Remove(key);
      if (profile is null)
      {
        return EAuthorizeResult.RemoveProfileFailed;
      }

      await WriteConfigurationAsync();

      // TODO modify/test
      // bool succ = await DeregisterAsync (profile);
      // EAuthorizeResult result = succ ? EAuthorizeResult.Succ : EAuthorizeResult.DeregistrationFailed;
      // return result;
      return EAuthorizeResult.Succ;
    }

    public async Task RefreshTokenAsync(IProfile profile) =>
      await RefreshTokenAsync(profile, false);

    public async Task<IEnumerable<IProfile>> GetRegisteredProfilesAsync()
    {
      if (Configuration is null)
      {
        await ReadConfigurationAsync();
      }

      return Configuration.GetSorted();
    }

    internal IProfile GetProfile(IProfileKey key) => Configuration?.Get(key);

    // internal instead of private for testing only
    internal async Task<(bool, IProfile)> AddProfileToConfig(Profile profile, string content)
    {
      bool succ = UpdateProfile(profile, content);
      if (!succ)
      {
        return (false, null);
      }

      await ReadConfigurationAsync();

      IProfile prevProfile = Configuration.AddOrReplace(profile);

      await WriteConfigurationAsync();

      return (true, prevProfile);
    }

    internal async Task WriteConfigurationAsync()
    {
      Log(3, this);

      if (Configuration is null)
      {
        return;
      }

      var result = GetTokenFunc?.Invoke();

      bool existed = Configuration.Existed;
      await Configuration.WriteAsync(result?.Token);

      if (!existed && (result?.Weak ?? false))
      {
        WeakConfigEncryptionCallback?.Invoke();
      }
    }

    internal async Task RefreshTokenAsync(IProfile profile, bool onAutoRefreshOnly)
    {
      using var logGuard = new LogGuard(3, this, () => $"auto={Settings?.AutoRefresh}, onAutoRefeshOnly={onAutoRefreshOnly}");
      EnsureHttpClient(profile);

      await ReadConfigurationAsync();

      if (onAutoRefreshOnly && (Settings?.AutoRefresh ?? false))
      {
        if (profile is Profile prof1 && (Configuration.Profiles?.Contains(prof1) ?? false))
        {
          await RefreshTokenCoreAsync(prof1);
        }
        else
        {
          Profile prof2 = Configuration.Profiles?.FirstOrDefault(d => d.Matches(profile));
          if (prof2 is not null)
          {
            await RefreshTokenCoreAsync(prof2);
          }
        }

        await WriteConfigurationAsync();
      }
    }

    // internal instead of private for testing only
    internal bool UpdateProfile(Profile profile, string json)
    {
      try
      {
        if (Logging.Level >= 3)
        {
          const string REGISTRATION = "RegistrationResponse";
          if (Logging.Level >= 4)
          {
            json.WriteTempJsonFile(REGISTRATION);
          }

          string jsonCleaned = json.ExtractJsonStructure();
          if (jsonCleaned is not null)
          {
            jsonCleaned.WriteTempJsonFile(REGISTRATION + "(cleared)");
          }
        }

        var root = Oahu.Audible.Json.RegistrationResponse.Deserialize(json);
        if (root is null)
        {
          return false;
        }

        var response = root.Response;
        var success = response.Success;
        var extensions = success.Extensions;
        var device_info = extensions.DeviceInfoJson;

        var deviceInfo = new DeviceInfo
        {
          Name = device_info.DeviceName,
          Type = device_info.DeviceType,
          Serial = device_info.DeviceSerialNumber
        };

        var customer_info = extensions.CustomerInfoJson;

        var customerInfo = new CustomerInfo
        {
          Name = customer_info.Name,
          GivenName = customer_info.GivenName,
          AccountId = customer_info.UserId
        };

        var tokens = success.Tokens;
        var website_cookies = tokens.WebsiteCookies;

        var cookies = new List<KeyValuePair<string, string>>();
        if (website_cookies is not null)
        {
          foreach (var cookie in website_cookies)
          {
            cookies.Add(new KeyValuePair<string, string>(
              cookie.Name,
              cookie.Value.Replace("\"", "")));
          }
        }

        var store_authentication_cookie = tokens.StoreAuthenticationCookie;
        string storeAuthentCookie = store_authentication_cookie.Cookie;

        var mac_dms = tokens.MacDms;
        string devicePrivateKey = mac_dms.DevicePrivateKey;
        string adpToken = mac_dms.AdpToken;

        var bearer = tokens.Bearer;
        int.TryParse(bearer.ExpiresIn, out var expires);

        var tokenBearer = new TokenBearer(
          bearer.AccessToken,
          bearer.RefreshToken,
          DateTime.UtcNow.AddSeconds(expires));

        profile.Update(
          tokenBearer,
          cookies,
          deviceInfo,
          customerInfo,
          devicePrivateKey,
          adpToken,
          storeAuthentCookie);
      }
      catch (Exception exc)
      {
        // Log (1, this, () => exc.Summary ());
        Log(1, this, () => exc.ToString());
        return false;
      }

      return true;
    }

    private HttpClientEx HttpClient(IProfile profile) => profile.PreAmazon ? HttpClientAudible : HttpClientAmazon;

    private void EnsureHttpClient(IProfile profile)
    {
      string domain = Locale.FromCountryCode(profile.Region).Domain;

      HttpClientAmazon = EnsureHttpClient(HttpAuthorityAmzn, HttpClientAmazon);
      HttpClientAudible = EnsureHttpClient(HttpAuthorityAdbl, HttpClientAudible);

      HttpClientEx EnsureHttpClient(string authority, HttpClientEx httpClient)
      {
        Uri baseUri = new Uri(authority + domain);

        if (httpClient is not null)
        {
          if (httpClient.BaseAddress == baseUri)
          {
            return httpClient;
          }
          else
          {
            httpClient.Dispose();
          }
        }

        return HttpClientEx.Create(baseUri);
      }
    }

    private async Task RefreshTokenCoreAsync(IProfile profile)
    {
      if (profile is null)
      {
        return;
      }

      using var logGuard = new LogGuard(3, this);

      try
      {
        if (DateTime.UtcNow < profile.Token.Expiration - TimeSpan.FromMinutes(5))
        {
          return;
        }

        Log(3, this, () => "from server");

        HttpRequestMessage request = BuildRefreshRequest(profile);

        var http = HttpClient(profile);

        await request.LogAsync(4, this, http.DefaultRequestHeaders, http.CookieContainer, http.BaseAddress);

        var response = await http.SendAsync(request);
        await response.LogAsync(4, this, http.CookieContainer, http.BaseAddress);

        response.EnsureSuccessStatusCode();
        string content = await response.Content.ReadAsStringAsync();

        RefreshToken(profile, content);
      }
      catch (Exception exc)
      {
        Log(1, this, () => exc.Summary());
      }
    }

    private HttpRequestMessage BuildRefreshRequest(IProfile profile)
    {
      var content = new Dictionary<string, string>
      {
        ["app_name"] = AppName,
        ["app_version"] = AppVersion,
        ["source_token"] = profile.Token.RefreshToken,
        ["requested_token_type"] = "access_token",
        ["source_token_type"] = "refresh_token"
      };

      var http = HttpClient(profile);

      Uri uri = new Uri(HttpPathToken, UriKind.Relative);

      var request = new HttpRequestMessage
      {
        Method = HttpMethod.Post,
        RequestUri = uri,
        Content = new FormUrlEncodedContent(content)
      };
      request.Headers.Add("x-amzn-identity-auth-domain", http.BaseAddress.Host);
      request.Headers.Add("Accept", "application/json");
      return request;
    }

    private void RefreshToken(IProfile profile, string json)
    {
      try
      {
        var jsonDoc = JsonDocument.Parse(json);
        var elRoot = jsonDoc.RootElement;

        string accessToken = elRoot.GetProperty("access_token").GetString();
        int expires = elRoot.GetProperty("expires_in").GetInt32();
        DateTime expiration = DateTime.UtcNow.AddSeconds(expires);

        var bearer = new TokenBearer(
          elRoot.GetProperty("access_token").GetString(),
          DateTime.UtcNow.AddSeconds(expires));

        profile.Refresh(bearer);
      }
      catch (Exception exc)
      {
        Log(1, this, () => exc.Summary());
      }
    }

    private async Task ReadConfigurationAsync()
    {
      using var logGuard = new LogGuard(3, this);

      if (Configuration is not null)
      {
        return;
      }

      Configuration = new Configuration();
      await ReadConfigAsync(false);
      if (Configuration.IsEncrypted)
      {
        await ReadConfigAsync(true);
      }

      async Task ReadConfigAsync(bool enforce)
      {
        var cfgToken = GetTokenFunc?.Invoke(enforce);
        await Configuration.ReadAsync(cfgToken?.Token);
      }
    }

    private HttpRequestMessage BuildRegisterRequest(IProfile profile)
    {
      ILocale locale = profile.Region.FromCountryCode();
      Uri uri = new Uri(HttpPathRegister, UriKind.Relative);
      string jsonBody = BuildRegisterBody(profile, locale);
      HttpContent content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
      var request = new HttpRequestMessage
      {
        Method = HttpMethod.Post,
        RequestUri = uri,
        Content = content
      };
      request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

      return request;
    }

    private HttpRequestMessage BuildDeregisterRequest(IProfile profile)
    {
      Uri uri = new Uri(HttpPathDeregister, UriKind.Relative);

      var content = new Dictionary<string, string>
      {
        ["deregister_all_existing_accounts"] = "false",
      };

      var request = new HttpRequestMessage
      {
        Method = HttpMethod.Post,
        RequestUri = uri,
        Content = new FormUrlEncodedContent(content)
      };
      request.Headers.Add("Authorization", $"Bearer {profile.Token.AccessToken}");
      request.Headers.Add("Accept", "application/json");

      return request;
    }

    private string BuildRegisterBody(IProfile profile, ILocale locale)
    {
      string json = $@"{{
        ""requested_token_type"":
            [""bearer"", ""mac_dms"", ""store_authentication_cookie"",
             ""website_cookies""],
        ""cookies"": {{
          ""domain"": ""https://www.audible.{locale.Domain}"",
          ""website_cookies"": []
        }},
        ""registration_data"": {{
          ""domain"": ""DeviceLegacy"",
          ""device_type"": ""{AudibleLogin.DeviceType}"",
          ""device_serial"": ""{profile.DeviceInfo.Serial}"",
          ""app_name"": ""{AppName}"",
          ""app_version"": ""{AppVersion}"",
          ""device_model"": ""{DeviceModel}"",
          ""os_version"": ""{OsVersion}"",
          ""software_version"": ""{SoftwareVersion}"",
          ""device_name"":
              ""%FIRST_NAME%%FIRST_NAME_POSSESSIVE_STRING%%DUPE_STRATEGY_1ST%Oahu""
        }},
        ""device_metadata"": {{
          ""device_os_family"": ""{OsFamily}"",
          ""device_type"": ""{AudibleLogin.DeviceType}"",
          ""device_serial"": ""{profile.DeviceInfo.Serial}"",
          ""manufacturer"": ""{Manufacturer}"",
          ""model"": ""{DeviceModel}"",
          ""os_version"": ""{OsVersionNumber}"",
          ""product"": ""{DeviceProduct}""
        }},
        ""auth_data"": {{
          ""use_global_authentication"": ""true"",
          ""authorization_code"": ""{profile.Authorization.AuthorizationCode}"",
          ""code_verifier"": ""{profile.Authorization.CodeVerifier}"",
          ""code_algorithm"": ""SHA-256"",
          ""client_domain"": ""DeviceLegacy"",
          ""client_id"": ""{AudibleLogin.BuildClientId(profile.DeviceInfo.Serial)}""
        }},
        ""requested_extensions"": [""device_info"", ""customer_info""]
      }}";

      if (Logging.Level >= 4)
      {
        string file = json.WriteTempTextFile();
        Log(4, this, () => $"buildRegisterBody: {file}");
      }

      json = json.CompactJson();

      if (!json.ValidateJson())
      {
        throw new InvalidOperationException("invalid json");
      }

      return json;
    }
  }
}
