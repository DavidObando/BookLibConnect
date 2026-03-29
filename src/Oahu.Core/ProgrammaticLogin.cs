using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Oahu.CommonTypes;
using Oahu.Core.Cryptography;
using static Oahu.Aux.Logging;

namespace Oahu.Core
{
  /// <summary>
  /// Implements programmatic login to Amazon/Audible by simulating the Android Audible app's
  /// authentication flow. This replaces the external browser approach that stopped working
  /// when Amazon began requiring session cookies and proper User-Agent before serving the
  /// sign-in page.
  /// </summary>
  internal class ProgrammaticLogin
  {
    const string BrowserUserAgent =
      "Mozilla/5.0 (Linux; Android 14; sdk_gphone64_x86_64 Build/UPB5.230623.003; wv) " +
      "AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/113.0.5672.136 Mobile Safari/537.36";

    const string MapVersion = "MAPAndroidLib-1.3.40908.0";
    const int MaxSessionCookieTries = 6;
    const int MaxRedirects = 15;

    /// <summary>
    /// Perform programmatic login, returning the redirect URI that contains the authorization code.
    /// </summary>
    public async Task<Uri> LoginAsync(
      ILocale locale,
      bool withPreAmazonUsername,
      Uri oauthUri,
      string deviceSerial,
      Credentials credentials,
      Callbacks callbacks)
    {
      var loginBaseUri = GetLoginBaseUri(locale, withPreAmazonUsername);
      var cookieDomain = GetCookieDomain(locale, withPreAmazonUsername);

      using var client = HttpClientEx.Create(loginBaseUri);
      ConfigureClient(client, locale, deviceSerial, cookieDomain);

      // GET the OAuth sign-in page directly (cookies frc/map-md/sid + UA are sufficient)
      Log(3, this, () => $"Getting OAuth page: {oauthUri}");
      var (AuthCodeUri, Response) = await GetFollowingRedirectsAsync(client, oauthUri, loginBaseUri);
      if (AuthCodeUri != null)
      {
        return AuthCodeUri;
      }

      Response.EnsureSuccessStatusCode();
      var html = await Response.Content.ReadAsStringAsync();

      // Parse form, submit credentials, handle challenges
      return await HandleLoginFlowAsync(client, loginBaseUri, html, credentials, callbacks, locale);
    }

    #region Client Setup

    private static Uri GetLoginBaseUri(ILocale locale, bool withPreAmazonUsername) =>
      withPreAmazonUsername
        ? new Uri($"https://www.audible.{locale.Domain}")
        : new Uri($"https://www.amazon.{locale.Domain}");

    private static string GetCookieDomain(ILocale locale, bool withPreAmazonUsername) =>
      withPreAmazonUsername
        ? $".audible.{locale.Domain}"
        : $".amazon.{locale.Domain}";

    private static string GetLanguage(ILocale locale) => locale.CountryCode switch
    {
      ERegion.De => "de-DE",
      ERegion.Fr => "fr-FR",
      ERegion.It => "it-IT",
      ERegion.Es => "es-ES",
      ERegion.Br => "pt-BR",
      ERegion.Jp => "ja-JP",
      ERegion.In => "en-IN",
      ERegion.Au => "en-AU",
      ERegion.Ca => "en-CA",
      ERegion.Uk => "en-GB",
      _ => "en-US",
    };

    private static void ConfigureClient(
      HttpClientEx client,
      ILocale locale,
      string deviceSerial,
      string cookieDomain)
    {
      client.Timeout = TimeSpan.FromSeconds(30);
      client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
      client.DefaultRequestHeaders.Add("Accept-Language", GetLanguage(locale));

      // Inject the three required cookies that Amazon checks before serving the sign-in page
      var frc = CreateFrcCookie(locale, deviceSerial);
      var mapMd = CreateMapMdCookie();
      client.CookieContainer.Add(new Cookie("frc", frc, "/ap", cookieDomain));
      client.CookieContainer.Add(new Cookie("map-md", mapMd, "/ap", cookieDomain));
      client.CookieContainer.Add(new Cookie("sid", "", "/", cookieDomain));
    }

    #endregion

    #region Cookie Generation

    private static string CreateFrcCookie(ILocale locale, string deviceSerial)
    {
      IPAddress ip;
      try
      {
        ip = NetworkInterface.GetAllNetworkInterfaces()
          .Select(i => i.GetIPProperties())
          .SelectMany(p =>
            p.DnsAddresses
              .Concat(p.GatewayAddresses.Select(a => a.Address))
              .Concat(p.UnicastAddresses.Select(a => a.Address)))
          .Where(a => a.AddressFamily is
            System.Net.Sockets.AddressFamily.InterNetwork or
            System.Net.Sockets.AddressFamily.InterNetworkV6)
          .OrderBy(a => a.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
          .OrderByDescending(a => !a.IsIPv6LinkLocal && !a.IsIPv6SiteLocal && !a.IsIPv6UniqueLocal)
          .FirstOrDefault() ?? IPAddress.IPv6Any;
      }
      catch
      {
        ip = IPAddress.IPv6Any;
      }

      var tz = DateTimeOffset.Now.Offset;
      var timeZone = (tz.Ticks < 0 ? "-" : "") + $"{tz:hh\\:mm}";

      var deviceInfo = new JsonObject
      {
        ["ApplicationName"] = Authorize.AppName,
        ["ApplicationVersion"] = "2090254511",
        ["DeviceOSVersion"] = Authorize.OsVersion,
        ["DeviceName"] = Authorize.DeviceModel,
        ["ScreenWidthPixels"] = "1344",
        ["ThirdPartyDeviceId"] = deviceSerial,
        ["FirstPartyDeviceId"] = deviceSerial,
        ["ScreenHeightPixels"] = "2769",
        ["DeviceLanguage"] = GetLanguage(locale),
        ["TimeZone"] = timeZone,
        ["Carrier"] = "T-Mobile",
        ["IpAddress"] = ip.ToString(),
      };

      return FrcEncoder.Encode(deviceSerial, deviceInfo.ToJsonString());
    }

    private static string CreateMapMdCookie()
    {
      var mapMd = new JsonObject
      {
        ["device_registration_data"] = new JsonObject
        {
          ["software_version"] = Authorize.SoftwareVersion,
        },
        ["app_identifier"] = new JsonObject
        {
          ["package"] = Authorize.AppName,
          ["SHA-256"] = null,
          ["app_version"] = Authorize.AppVersion,
          ["app_version_name"] = Authorize.AppVersionName,
          ["app_sms_hash"] = null,
          ["map_version"] = MapVersion,
        },
        ["app_info"] = new JsonObject
        {
          ["auto_pv"] = 0,
          ["auto_pv_with_smsretriever"] = 1,
          ["smartlock_supported"] = 0,
          ["permission_runtime_grant"] = 2,
        },
      };

      return Convert.ToBase64String(Encoding.UTF8.GetBytes(mapMd.ToJsonString()));
    }

    #endregion

    #region Session Establishment

    private static async Task LoadSessionCookiesAsync(HttpClientEx client, Uri baseUri)
    {
      for (int i = 0; i < MaxSessionCookieTries; i++)
      {
        // Follow redirects to collect all cookies from the chain.
        // HttpClientEx has AllowAutoRedirect=false, so we follow manually.
        var response = await client.GetAsync(baseUri);
        int redirects = 0;
        while (IsRedirect(response.StatusCode) && redirects < 10)
        {
          var location = response.Headers.Location;
          if (location == null)
          {
            break;
          }

          var target = location.IsAbsoluteUri ? location : new Uri(baseUri, location);
          response = await client.GetAsync(target);
          redirects++;
        }

        var cookies = client.CookieContainer.GetCookies(baseUri);
        if (cookies.Cast<Cookie>().Any(c => c.Name.Equals("session-token", StringComparison.OrdinalIgnoreCase)))
        {
          Log(3, typeof(ProgrammaticLogin), () => $"Session token obtained after {i + 1} tries");
          return;
        }
      }

      throw new TimeoutException(
        $"Failed to obtain session-token cookie after {MaxSessionCookieTries} attempts");
    }

    #endregion

    #region HTTP Helpers

    private static async Task<(Uri AuthCodeUri, HttpResponseMessage Response)> GetFollowingRedirectsAsync(
      HttpClientEx client,
      Uri uri,
      Uri baseUri)
    {
      var response = await client.GetAsync(uri);
      return await FollowRedirectsAsync(client, response, baseUri);
    }

    private static async Task<(Uri AuthCodeUri, HttpResponseMessage Response)> SubmitFormAsync(
      HttpClientEx client,
      Uri baseUri,
      string action,
      string method,
      Dictionary<string, string> inputs)
    {
      var requestUri = action.StartsWith("http", StringComparison.OrdinalIgnoreCase)
        ? new Uri(action)
        : new Uri(baseUri, action);

      HttpResponseMessage response;
      if (method.Equals("POST", StringComparison.OrdinalIgnoreCase))
      {
        response = await client.PostAsync(requestUri, new FormUrlEncodedContent(inputs));
      }
      else
      {
        var query = string.Join("&",
          inputs.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
        response = await client.GetAsync(new Uri($"{requestUri}?{query}"));
      }

      return await FollowRedirectsAsync(client, response, baseUri);
    }

    private static async Task<(Uri AuthCodeUri, HttpResponseMessage Response)> FollowRedirectsAsync(
      HttpClientEx client,
      HttpResponseMessage response,
      Uri baseUri)
    {
      int redirectCount = 0;

      while (IsRedirect(response.StatusCode) && redirectCount < MaxRedirects)
      {
        var location = response.Headers.Location;
        if (location == null)
        {
          break;
        }

        var absoluteUri = location.IsAbsoluteUri ? location : new Uri(baseUri, location);

        // Check if this redirect carries the authorization code
        if (absoluteUri.Query.Contains("openid.oa2.authorization_code") ||
            absoluteUri.AbsolutePath.Contains("maplanding"))
        {
          return (absoluteUri, response);
        }

        response = await client.GetAsync(absoluteUri);
        redirectCount++;
      }

      return (null, response);
    }

    private static bool IsRedirect(HttpStatusCode code) => code is
      HttpStatusCode.MovedPermanently or
      HttpStatusCode.Found or
      HttpStatusCode.SeeOther or
      HttpStatusCode.TemporaryRedirect or
      HttpStatusCode.PermanentRedirect;

    #endregion

    #region HTML Form Parsing

    private static (string Action, string Method, Dictionary<string, string> Inputs) ParseForm(string html)
    {
      // Find the sign-in form by name, then fall back to first POST form, then any form
      string formContent = null;
      string formTag = null;

      var patterns = new[]
      {
        @"(<form\b[^>]*\bname\s*=\s*""signIn""[^>]*>)([\s\S]*?)</form>",
        @"(<form\b[^>]*\bmethod\s*=\s*""[Pp][Oo][Ss][Tt]""[^>]*>)([\s\S]*?)</form>",
        @"(<form\b[^>]*>)([\s\S]*?)</form>",
      };

      foreach (var pattern in patterns)
      {
        var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
        if (match.Success)
        {
          formTag = match.Groups[1].Value;
          formContent = match.Groups[2].Value;
          break;
        }
      }

      if (formTag == null || formContent == null)
      {
        return (null, null, null);
      }

      // Extract action and method from the form tag
      var actionMatch = Regex.Match(formTag, @"\baction\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase);
      var methodMatch = Regex.Match(formTag, @"\bmethod\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase);

      string action = actionMatch.Success ? WebUtility.HtmlDecode(actionMatch.Groups[1].Value) : null;
      string method = methodMatch.Success ? methodMatch.Groups[1].Value.ToUpperInvariant() : "GET";

      // Extract all input name/value pairs
      var inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      var inputMatches = Regex.Matches(formContent, @"<input\b[^>]*>", RegexOptions.IgnoreCase);

      foreach (Match inputMatch in inputMatches)
      {
        var nameMatch = Regex.Match(inputMatch.Value, @"\bname\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase);
        if (!nameMatch.Success)
        {
          continue;
        }

        var valueMatch = Regex.Match(inputMatch.Value, @"\bvalue\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase);
        var name = WebUtility.HtmlDecode(nameMatch.Groups[1].Value);
        var value = valueMatch.Success ? WebUtility.HtmlDecode(valueMatch.Groups[1].Value) : "";

        inputs[name] = value;
      }

      return (action, method, inputs);
    }

    #endregion

    #region Metadata Generation

    private static string GenerateEncryptedMetadata(ILocale locale, Uri loginUri)
    {
      long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
      var raw = GenerateMetadata(loginUri.ToString(), now);
      return MetadataEncryptor.Encrypt(raw);
    }

    private static string GenerateMetadata(string loginUrl, long nowUnixTimeStamp)
    {
      var metadata = new JsonObject
      {
        ["start"] = nowUnixTimeStamp,
        ["interaction"] = new JsonObject
        {
          ["keys"] = 0,
          ["keyPressTimeIntervals"] = new JsonArray(),
          ["copies"] = 0,
          ["cuts"] = 0,
          ["pastes"] = 0,
          ["clicks"] = 0,
          ["touches"] = 0,
          ["mouseClickPositions"] = new JsonArray(),
          ["keyCycles"] = new JsonArray(),
          ["mouseCycles"] = new JsonArray(),
          ["touchCycles"] = new JsonArray(),
        },
        ["version"] = "3.0.0",
        ["lsUbid"] = "X39-6721012-8795219:1549849158",
        ["timeZone"] = -6,
        ["scripts"] = new JsonObject
        {
          ["dynamicUrls"] = new JsonArray(
            "https://images-na.ssl-images-amazon.com/images/I/61HHaoAEflL._RC|11-BZEJ8lnL.js,01qkmZhGmAL.js,71qOHv6nKaL.js_.js?AUIClients/AudibleiOSMobileWhiteAuthSkin#mobile",
            "https://images-na.ssl-images-amazon.com/images/I/21T7I7qVEeL._RC|21T1XtqIBZL.js,21WEJWRAQlL.js,31DwnWh8lFL.js,21VKEfzET-L.js,01fHQhWQYWL.js,51TfwrUQAQL.js_.js?AUIClients/AuthenticationPortalAssets#mobile",
            "https://images-na.ssl-images-amazon.com/images/I/0173Lf6yxEL.js?AUIClients/AuthenticationPortalInlineAssets",
            "https://images-na.ssl-images-amazon.com/images/I/211S6hvLW6L.js?AUIClients/CVFAssets",
            "https://images-na.ssl-images-amazon.com/images/G/01/x-locale/common/login/fwcim._CB454428048_.js"),
          ["inlineHashes"] = new JsonArray(
            -1746719145, 1334687281, -314038750, 1184642547, -137736901,
            318224283, 585973559, 1103694443, 11288800, -1611905557,
            1800521327, -1171760960, -898892073),
          ["elapsed"] = 52,
          ["dynamicUrlCount"] = 5,
          ["inlineHashesCount"] = 13,
        },
        ["plugins"] = "unknown||320-568-548-32-*-*-*",
        ["dupedPlugins"] = "unknown||320-568-548-32-*-*-*",
        ["screenInfo"] = "320-568-548-32-*-*-*",
        ["capabilities"] = new JsonObject
        {
          ["js"] = new JsonObject
          {
            ["audio"] = true,
            ["geolocation"] = true,
            ["localStorage"] = "supported",
            ["touch"] = true,
            ["video"] = true,
            ["webWorker"] = true,
          },
          ["css"] = new JsonObject
          {
            ["textShadow"] = true,
            ["textStroke"] = true,
            ["boxShadow"] = true,
            ["borderRadius"] = true,
            ["borderImage"] = true,
            ["opacity"] = true,
            ["transform"] = true,
            ["transition"] = true,
          },
          ["elapsed"] = 1,
        },
        ["referrer"] = "",
        ["userAgent"] = BrowserUserAgent,
        ["location"] = loginUrl,
        ["webDriver"] = null,
        ["history"] = new JsonObject
        {
          ["length"] = 1,
        },
        ["gpu"] = new JsonObject
        {
          ["vendor"] = "Apple Inc.",
          ["model"] = "Apple A9 GPU",
          ["extensions"] = new JsonArray(),
        },
        ["math"] = new JsonObject
        {
          ["tan"] = "-1.4214488238747243",
          ["sin"] = "0.8178819121159085",
          ["cos"] = "-0.5753861119575491",
        },
        ["performance"] = new JsonObject
        {
          ["timing"] = new JsonObject
          {
            ["navigationStart"] = nowUnixTimeStamp,
            ["unloadEventStart"] = 0,
            ["unloadEventEnd"] = 0,
            ["redirectStart"] = 0,
            ["redirectEnd"] = 0,
            ["fetchStart"] = nowUnixTimeStamp,
            ["domainLookupStart"] = nowUnixTimeStamp,
            ["domainLookupEnd"] = nowUnixTimeStamp,
            ["connectStart"] = nowUnixTimeStamp,
            ["connectEnd"] = nowUnixTimeStamp,
            ["secureConnectionStart"] = nowUnixTimeStamp,
            ["requestStart"] = nowUnixTimeStamp,
            ["responseStart"] = nowUnixTimeStamp,
            ["responseEnd"] = nowUnixTimeStamp,
            ["domLoading"] = nowUnixTimeStamp,
            ["domInteractive"] = nowUnixTimeStamp,
            ["domContentLoadedEventStart"] = nowUnixTimeStamp,
            ["domContentLoadedEventEnd"] = nowUnixTimeStamp,
            ["domComplete"] = nowUnixTimeStamp,
            ["loadEventStart"] = nowUnixTimeStamp,
            ["loadEventEnd"] = nowUnixTimeStamp,
          },
        },
        ["end"] = nowUnixTimeStamp,
        ["timeToSubmit"] = 108873,
        ["form"] = new JsonObject
        {
          ["email"] = new JsonObject
          {
            ["keys"] = 0,
            ["keyPressTimeIntervals"] = new JsonArray(),
            ["copies"] = 0,
            ["cuts"] = 0,
            ["pastes"] = 0,
            ["clicks"] = 0,
            ["touches"] = 0,
            ["mouseClickPositions"] = new JsonArray(),
            ["keyCycles"] = new JsonArray(),
            ["mouseCycles"] = new JsonArray(),
            ["touchCycles"] = new JsonArray(),
            ["width"] = 290,
            ["height"] = 43,
            ["checksum"] = "C860E86B",
            ["time"] = 12773,
            ["autocomplete"] = false,
            ["prefilled"] = false,
          },
          ["password"] = new JsonObject
          {
            ["keys"] = 0,
            ["keyPressTimeIntervals"] = new JsonArray(),
            ["copies"] = 0,
            ["cuts"] = 0,
            ["pastes"] = 0,
            ["clicks"] = 0,
            ["touches"] = 0,
            ["mouseClickPositions"] = new JsonArray(),
            ["keyCycles"] = new JsonArray(),
            ["mouseCycles"] = new JsonArray(),
            ["touchCycles"] = new JsonArray(),
            ["width"] = 290,
            ["height"] = 43,
            ["time"] = 10353,
            ["autocomplete"] = false,
            ["prefilled"] = false,
          },
        },
        ["canvas"] = new JsonObject
        {
          ["hash"] = -373378155,
          ["emailHash"] = -1447130560,
          ["histogramBins"] = new JsonArray(),
        },
        ["token"] = null,
        ["errors"] = new JsonArray(),
        ["metrics"] = new JsonArray(
          new JsonObject { ["n"] = "fwcim-mercury-collector", ["t"] = 0 },
          new JsonObject { ["n"] = "fwcim-instant-collector", ["t"] = 0 },
          new JsonObject { ["n"] = "fwcim-element-telemetry-collector", ["t"] = 2 },
          new JsonObject { ["n"] = "fwcim-script-version-collector", ["t"] = 0 },
          new JsonObject { ["n"] = "fwcim-local-storage-identifier-collector", ["t"] = 0 },
          new JsonObject { ["n"] = "fwcim-timezone-collector", ["t"] = 0 },
          new JsonObject { ["n"] = "fwcim-script-collector", ["t"] = 1 },
          new JsonObject { ["n"] = "fwcim-plugin-collector", ["t"] = 0 },
          new JsonObject { ["n"] = "fwcim-capability-collector", ["t"] = 1 },
          new JsonObject { ["n"] = "fwcim-browser-collector", ["t"] = 0 },
          new JsonObject { ["n"] = "fwcim-history-collector", ["t"] = 0 },
          new JsonObject { ["n"] = "fwcim-gpu-collector", ["t"] = 1 },
          new JsonObject { ["n"] = "fwcim-battery-collector", ["t"] = 0 },
          new JsonObject { ["n"] = "fwcim-dnt-collector", ["t"] = 0 },
          new JsonObject { ["n"] = "fwcim-math-fingerprint-collector", ["t"] = 0 },
          new JsonObject { ["n"] = "fwcim-performance-collector", ["t"] = 0 },
          new JsonObject { ["n"] = "fwcim-timer-collector", ["t"] = 0 },
          new JsonObject { ["n"] = "fwcim-time-to-submit-collector", ["t"] = 0 },
          new JsonObject { ["n"] = "fwcim-form-input-telemetry-collector", ["t"] = 4 },
          new JsonObject { ["n"] = "fwcim-canvas-collector", ["t"] = 2 },
          new JsonObject { ["n"] = "fwcim-captcha-telemetry-collector", ["t"] = 0 },
          new JsonObject { ["n"] = "fwcim-proof-of-work-collector", ["t"] = 1 },
          new JsonObject { ["n"] = "fwcim-ubf-collector", ["t"] = 0 },
          new JsonObject { ["n"] = "fwcim-timer-collector", ["t"] = 0 }),
      };

      return metadata.ToJsonString();
    }

    #endregion

    #region Login Flow

    private async Task<Uri> HandleLoginFlowAsync(
      HttpClientEx client,
      Uri baseUri,
      string html,
      Credentials credentials,
      Callbacks callbacks,
      ILocale locale)
    {
      // Parse the login form
      var (action, method, inputs) = ParseForm(html);
      if (action == null)
      {
        throw new InvalidOperationException("Could not parse login form from response");
      }

      // Set credentials and encrypted metadata
      inputs["email"] = credentials.Username;
      inputs["password"] = credentials.Password;
      inputs["metadata1"] = GenerateEncryptedMetadata(locale, baseUri);

      Log(3, this, () => "Submitting credentials...");

      // Submit and process result
      var (authCodeUri, response) = await SubmitFormAsync(client, baseUri, action, method, inputs);
      if (authCodeUri != null)
      {
        return authCodeUri;
      }

      var responseHtml = await response.Content.ReadAsStringAsync();
      return await ProcessChallengeAsync(client, baseUri, responseHtml, credentials, callbacks, locale);
    }

    private async Task<Uri> ProcessChallengeAsync(
      HttpClientEx client,
      Uri baseUri,
      string html,
      Credentials credentials,
      Callbacks callbacks,
      ILocale locale)
    {
      // Captcha
      if (html.Contains("auth-captcha-image", StringComparison.OrdinalIgnoreCase) ||
          html.Contains("captchacharacters", StringComparison.OrdinalIgnoreCase))
      {
        Log(3, this, () => "Captcha detected");
        return await HandleCaptchaAsync(client, baseUri, html, credentials, callbacks, locale);
      }

      // MFA / OTP
      if (html.Contains("auth-mfa-form", StringComparison.OrdinalIgnoreCase) ||
          html.Contains("otpCode", StringComparison.OrdinalIgnoreCase))
      {
        Log(3, this, () => "MFA detected");
        return await HandleMfaAsync(client, baseUri, html, callbacks, locale);
      }

      // CVF (Customer Verification Flow)
      if (html.Contains("cvf-widget", StringComparison.OrdinalIgnoreCase) ||
          html.Contains("auth-verify", StringComparison.OrdinalIgnoreCase))
      {
        Log(3, this, () => "CVF detected");
        return await HandleCvfAsync(client, baseUri, html, callbacks, locale);
      }

      // Approval alert (approve on another device)
      if (html.Contains("auth-approve-form", StringComparison.OrdinalIgnoreCase) ||
          html.Contains("approval-alert", StringComparison.OrdinalIgnoreCase))
      {
        Log(3, this, () => "Approval required");
        return await HandleApprovalAsync(client, baseUri, html, callbacks, locale);
      }

      // Check for error messages
      var errorMatch = Regex.Match(html,
        @"<div[^>]*class=""[^""]*a-alert-content[^""]*""[^>]*>(.*?)</div>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase);
      string errorText = errorMatch.Success
        ? Regex.Replace(errorMatch.Groups[1].Value, @"<[^>]+>", "").Trim()
        : null;

      if (!string.IsNullOrWhiteSpace(errorText))
      {
        Log(1, this, () => $"Login error: {errorText}");
      }

      throw new InvalidOperationException(
        $"Unexpected login response. {(errorText != null ? $"Error: {errorText}" : "Could not determine next step.")}");
    }

    #endregion

    #region Challenge Handlers

    private async Task<Uri> HandleCaptchaAsync(
      HttpClientEx client,
      Uri baseUri,
      string html,
      Credentials credentials,
      Callbacks callbacks,
      ILocale locale)
    {
      if (callbacks.CaptchaCallback == null)
      {
        throw new InvalidOperationException("Captcha required but no CaptchaCallback provided");
      }

      // Extract captcha image URL
      var imgMatch = Regex.Match(html,
        @"<img[^>]*\bid\s*=\s*""auth-captcha-image""[^>]*\bsrc\s*=\s*""([^""]*)""",
        RegexOptions.IgnoreCase);
      if (!imgMatch.Success)
      {
        imgMatch = Regex.Match(html,
          @"<img[^>]*\bsrc\s*=\s*""(https?://[^""]*captcha[^""]*)""",
          RegexOptions.IgnoreCase);
      }

      if (!imgMatch.Success)
      {
        throw new InvalidOperationException("Captcha required but could not find captcha image");
      }

      var imageUrl = WebUtility.HtmlDecode(imgMatch.Groups[1].Value);
      var imageBytes = await client.GetByteArrayAsync(imageUrl);

      var answer = callbacks.CaptchaCallback(imageBytes);
      if (string.IsNullOrEmpty(answer))
      {
        throw new OperationCanceledException("Captcha not solved by user");
      }

      // Parse form and submit with captcha answer
      var (action, method, inputs) = ParseForm(html);
      if (action == null)
      {
        throw new InvalidOperationException("Could not parse captcha form");
      }

      // Amazon uses either "guess" or "captchacharacters" for the captcha input
      if (inputs.ContainsKey("guess"))
      {
        inputs["guess"] = answer;
      }
      else
      {
        inputs["captchacharacters"] = answer;
      }

      inputs["metadata1"] = GenerateEncryptedMetadata(locale, baseUri);

      var (authCodeUri, response) = await SubmitFormAsync(client, baseUri, action, method, inputs);
      if (authCodeUri != null)
      {
        return authCodeUri;
      }

      var responseHtml = await response.Content.ReadAsStringAsync();
      return await ProcessChallengeAsync(client, baseUri, responseHtml, credentials, callbacks, locale);
    }

    private async Task<Uri> HandleMfaAsync(
      HttpClientEx client,
      Uri baseUri,
      string html,
      Callbacks callbacks,
      ILocale locale)
    {
      if (callbacks.MfaCallback == null)
      {
        throw new InvalidOperationException("MFA required but no MfaCallback provided");
      }

      var code = callbacks.MfaCallback();
      if (string.IsNullOrEmpty(code))
      {
        throw new OperationCanceledException("MFA code not provided by user");
      }

      var (action, method, inputs) = ParseForm(html);
      if (action == null)
      {
        throw new InvalidOperationException("Could not parse MFA form");
      }

      inputs["otpCode"] = code;
      inputs["metadata1"] = GenerateEncryptedMetadata(locale, baseUri);

      var (authCodeUri, response) = await SubmitFormAsync(client, baseUri, action, method, inputs);
      if (authCodeUri != null)
      {
        return authCodeUri;
      }

      var responseHtml = await response.Content.ReadAsStringAsync();
      return await ProcessChallengeAsync(client, baseUri, responseHtml, null, callbacks, locale);
    }

    private async Task<Uri> HandleCvfAsync(
      HttpClientEx client,
      Uri baseUri,
      string html,
      Callbacks callbacks,
      ILocale locale)
    {
      if (callbacks.CvfCallback == null)
      {
        throw new InvalidOperationException("CVF required but no CvfCallback provided");
      }

      var code = callbacks.CvfCallback();
      if (string.IsNullOrEmpty(code))
      {
        throw new OperationCanceledException("CVF code not provided by user");
      }

      var (action, method, inputs) = ParseForm(html);
      if (action == null)
      {
        throw new InvalidOperationException("Could not parse CVF form");
      }

      // Set the verification code in the appropriate field
      if (inputs.ContainsKey("code"))
      {
        inputs["code"] = code;
      }
      else if (inputs.ContainsKey("otpCode"))
      {
        inputs["otpCode"] = code;
      }
      else
      {
        inputs["cvf_challenge_response"] = code;
      }

      var (authCodeUri, response) = await SubmitFormAsync(client, baseUri, action, method, inputs);
      if (authCodeUri != null)
      {
        return authCodeUri;
      }

      var responseHtml = await response.Content.ReadAsStringAsync();
      return await ProcessChallengeAsync(client, baseUri, responseHtml, null, callbacks, locale);
    }

    private async Task<Uri> HandleApprovalAsync(
      HttpClientEx client,
      Uri baseUri,
      string html,
      Callbacks callbacks,
      ILocale locale)
    {
      callbacks.ApprovalCallback?.Invoke();

      // Parse the approval form and submit (user has approved on their device)
      var (action, method, inputs) = ParseForm(html);
      if (action == null)
      {
        throw new InvalidOperationException("Could not parse approval form");
      }

      var (authCodeUri, response) = await SubmitFormAsync(client, baseUri, action, method, inputs);
      if (authCodeUri != null)
      {
        return authCodeUri;
      }

      var responseHtml = await response.Content.ReadAsStringAsync();
      return await ProcessChallengeAsync(client, baseUri, responseHtml, null, callbacks, locale);
    }

    #endregion
  }
}
