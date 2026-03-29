using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Oahu.Aux.Extensions;
using Oahu.CommonTypes;
using Oahu.Core.Ex;
using static Oahu.Aux.Logging;

namespace Oahu.Core
{
  class AudibleLogin
  {
    public const string DeviceType = "A10KISP2GWF0E4";

    public ERegion Region { get; private set; }

    public bool WithPreAmazonUsername { get; private set; }

    public string CodeVerifierB64 { get; private set; }

    public string CodeChallengeB64 { get; private set; }

    public string Serial { get; private set; }

    public string ClientId { get; private set; }

    public Uri BuildAuthUri(
      ERegion region,
      bool withPreAmazonUsername)
    {
      Log(3, this, () => $"reg={region}, preAmznAccnt={withPreAmazonUsername}");

      Region = region;
      var locale = region.FromCountryCode();
      WithPreAmazonUsername = withPreAmazonUsername;

      if (withPreAmazonUsername && !(new[] { ERegion.De, ERegion.Uk, ERegion.Us }.Contains(locale.CountryCode)))
      {
        throw new ArgumentException("Login with username is only supported for DE, US and UK marketplaces!");
      }

      Serial = BuildDeviceSerial();
      ClientId = BuildClientId(Serial);

      CodeVerifierB64 = CreateCodeVerifier();
      CodeChallengeB64 = CreateSHA256CodeChallenge(CodeVerifierB64);

      // return_to is always audible.{TLD} per AudibleApi's static analysis of Audible Android APK
      string return_to = $"https://www.audible.{locale.Domain}/ap/maplanding";
      string cc = locale.CountryCode.ToString().ToLowerInvariant();
      string base_url, assoc_handle, page_id;
      if (withPreAmazonUsername)
      {
        base_url = $"https://www.audible.{locale.Domain}/ap/signin";
        assoc_handle = $"amzn_audible_android_aui_lap_{cc}";
        page_id = $"amzn_audible_android_privatepool_aui_v2_dark_{cc}";
      }
      else
      {
        base_url = $"https://www.amazon.{locale.Domain}/ap/signin";
        assoc_handle = $"amzn_audible_android_aui_{cc}";
        page_id = $"amzn_audible_android_aui_v2_dark_us{cc}";
      }

      var oauthParams = new List<KeyValuePair<string, string>>()
      {
        new("openid.pape.max_auth_age", "0"),
        new("openid.identity", "http://specs.openid.net/auth/2.0/identifier_select"),
        new("accountStatusPolicy", "P1"),
        new("marketPlaceId", locale.MarketPlaceId),
        new("pageId", page_id),
        new("openid.return_to", return_to),
        new("openid.assoc_handle", assoc_handle),
        new("openid.oa2.response_type", "code"),
        new("openid.mode", "checkid_setup"),
        new("openid.ns.pape", "http://specs.openid.net/extensions/pape/1.0"),
        new("openid.oa2.code_challenge_method", "S256"),
        new("openid.ns.oa2", "http://www.amazon.com/ap/ext/oauth/2"),
        new("openid.oa2.code_challenge", CodeChallengeB64),
        new("openid.oa2.scope", "device_auth_access"),
        new("openid.claimed_id", "http://specs.openid.net/auth/2.0/identifier_select"),
        new("openid.oa2.client_id", $"device:{ClientId}"),
        new("disableLoginPrepopulate", "1"),
        new("openid.ns", "http://specs.openid.net/auth/2.0"),
      };

      return new Uri($"{base_url}?{oauthParams.ToQueryString()}");
    }

    public Profile ParseExternalResponse(Uri uri)
    {
      var authorization = Authorization.Create(uri);
      if (authorization is null)
      {
        return null;
      }

      authorization.CodeVerifier = CodeVerifierB64;

      return new Profile(Region, authorization, Serial, WithPreAmazonUsername);
    }

    // internal instead of private for testing only
    internal static string BuildDeviceSerial()
    {
      byte[] serialBytes = new byte[20];
      Random.Shared.NextBytes(serialBytes);
      string serial = Convert.ToHexString(serialBytes).ToLower();
      Log(3, typeof(AudibleLogin), () => serial);
      return serial;
    }

    // internal instead of private for testing only
    internal static string BuildClientId(string serial)
    {
      string serialEx = $"{serial}#{DeviceType}";
      byte[] clientId = Encoding.UTF8.GetBytes(serialEx);
      string clientIdHex = Convert.ToHexString(clientId).ToLower();
      Log(3, typeof(AudibleLogin), () => clientIdHex);
      return clientIdHex;
    }

    // internal instead of private for testing only
    internal static string CreateCodeVerifier()
    {
      byte[] tokenBytes = new byte[32];
      Random.Shared.NextBytes(tokenBytes);
      string codeVerifier = tokenBytes.ToUrlBase64String();
      return codeVerifier;
    }

    // internal instead of private for testing only
    internal static string CreateSHA256CodeChallenge(string codeVerifier)
    {
      var sha256 = SHA256.Create();
      var tokenBytes = codeVerifier.GetBytes();
      var hash = sha256.ComputeHash(tokenBytes);
      return hash.ToUrlBase64String();
    }
  }
}
