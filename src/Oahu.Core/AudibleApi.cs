// #define TEST_UNAVAIL
// #define TEST_INVAL_CHAR

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Oahu.Aux;
using Oahu.Aux.Extensions;
using Oahu.BooksDatabase;
using Oahu.CommonTypes;
using Oahu.Core.Ex;
using Oahu.Decrypt;
using static Oahu.Aux.Logging;
using R = Oahu.Core.Properties.Resources;

namespace Oahu.Core
{
  class AudibleApi : IAudibleApi
  {
    const string UserAgent = "com.audible.playersdk.player/3.96.1 (Linux;Android 14) AndroidXMedia3/1.3.0";

    // const string HTTP_AUTHORITY_AUDIBLE = @"https://api.audible.";
    const string ContentPath = "/1.0/content";
    private int accountId;
    private string accountAlias;

#if TEST_INVAL_CHAR
    const char ORIG = '\'';
    const char SUBS = '.';
#endif

    public AudibleApi(
      IProfile profile,
      HttpClientEx httpClientAmazon,
      HttpClientEx httpClientAudible,
      BookLibrary bookLibrary,
      Func<IProfile, Task> refreshTokenAsyncFunc)
    {
      BookLibrary = bookLibrary;
      RefreshTokenAsyncFunc = () => refreshTokenAsyncFunc(Profile);

      if (profile is null)
      {
        return;
      }

      Profile = profile;
      HttpClientAmazon = httpClientAmazon;
      HttpClientAudible = httpClientAudible;

      // ILocale locale = profile.Region.FromCountryCode ();
      // Uri baseUriAudible = new Uri (HTTP_AUTHORITY_AUDIBLE + locale.Domain);
      // HttpClientAudible = HttpClientEx.Create (baseUriAudible);
    }

    internal AudibleApi(
      BookLibrary bookLibrary,
      int accountId,
      ERegion region)
    {
      BookLibrary = bookLibrary;
      this.accountId = accountId;
      Profile = new Profile(region, null, null, false);
    }

    public string AccountAlias
    {
      get
      {
        EnsureAccountId();
        return accountAlias;
      }
    }

    public ERegion Region => Profile.Region;

    public Func<AccountAliasContext, bool> GetAccountAliasFunc { private get; set; }

    public Func<Task> RefreshTokenAsyncFunc { get; private set; }

    internal bool HasAdpToken => !string.IsNullOrEmpty(Profile?.AdpToken);

    internal bool HasPrivateKey => !string.IsNullOrEmpty(Profile?.PrivateKey);

    internal bool HasAccessToken => !string.IsNullOrEmpty(Profile?.Token?.AccessToken);

    // private string BaseUrlAudible { get; }
    // private Uri BaseUriAudible => HttpClientAudible?.BaseAddress;
    // private Uri BaseUriAmazon => HttpClientAmazon?.BaseAddress;
    private IProfile Profile { get; }

    private HttpClientEx HttpClientAudible { get; }

    private HttpClientEx HttpClientAmazon { get; }

    private BookLibrary BookLibrary { get; }

    private int AccountId
    {
      get
      {
        EnsureAccountId();
        return accountId;
      }
    }

    private HttpClientEx HttpClient => Profile.PreAmazon ? HttpClientAudible : HttpClientAmazon;

    public void Dispose()
    {
      // HttpClientAudible?.Dispose ();
    }

    public async Task<Oahu.Audible.Json.LibraryResponse> GetLibraryAsync(bool resync) => await GetLibraryAsync(null, resync);

    public async Task<string> GetUserProfileAsync()
    {
      using var logGuard = new LogGuard(3, this);

      await RefreshTokenAsyncFunc();

      var url = $"/user/profile?access_token={Profile.Token.AccessToken}";

      var request = new HttpRequestMessage(HttpMethod.Get, url);
      return await SendForStringAsync(request, HttpClient);
    }

    public async Task<string> GetAccountInfoAsync()
    {
      using var logGuard = new LogGuard(3, this);

      const string GROUPS = "response_groups=migration_details,subscription_details_rodizio,subscription_details_premium,customer_segment,subscription_details_channels";

      var url = "/1.0/customer/information"
        + "?"
        + GROUPS;

      return await CallAudibleApiSignedForStringAsync(url);
    }

    public async Task<bool> GetActivationBytesAsync()
    {
      using var logGuard = new LogGuard(3, this);
      var url = "/license/token?action=register&player_manuf=Audible,Android&player_model=Android";
      byte[] response = await CallAudibleApiSignedForBytesAsync(url);

      return false;
    }

    public async Task<Oahu.Audible.Json.LicenseResponse> GetDownloadLicenseAsync(string asin, EDownloadQuality quality)
    {
      using var logGuard = new LogGuard(3, this, () => $"asin={asin}");
      string response = await GetDownloadLicenseAsyncInternal(asin, quality);

      if (Logging.Level >= 3)
      {
        string file = response.WriteTempJsonFile($"LicenseResponse_{asin}");
        Log(3, this, () => $"asin={asin}, file=\"{Path.GetFileName(file)}\"");
      }

      Oahu.Audible.Json.LicenseResponse license = Oahu.Audible.Json.LicenseResponse.Deserialize(response);

      DecryptLicense(license?.ContentLicense);

      return license;
    }

    public async Task<bool> GetDownloadLicenseAndSaveAsync(Conversion conversion, EDownloadQuality quality)
    {
      using var logGuard = new LogGuard(3, this, () => $"{conversion}");
      Log(3, this, () => $"{conversion}; desired quality: {quality}");
      Oahu.Audible.Json.LicenseResponse licresp;

      // get license
      try
      {
        licresp = await GetDownloadLicenseAsync(conversion.Asin, quality);
      }
      catch (Exception exc)
      {
        conversion.State = EConversionState.LicenseDenied;
        Log(3, this, () => $"{conversion}; {exc.Summary()}");
        return false;
      }

      var lic = licresp?.ContentLicense;
      if (lic?.Voucher is null)
      {
        conversion.State = EConversionState.LicenseDenied;
        Log(3, this, () => $"{conversion}; license decryption failed.");
        return false;
      }

      bool succ = Enum.TryParse<ELicenseStatusCode>(lic.StatusCode, out var status);
      if (!succ || status != ELicenseStatusCode.Granted)
      {
        conversion.State = EConversionState.LicenseDenied;
        Log(3, this, () => $"{conversion}; license not granted.");
        return false;
      }

      // save license to DB, including chapters
      // update state
      var aq = BookLibrary.UpdateLicenseAndChapters(lic, conversion, quality);
      Log(3, this, () => $"{conversion}; done, {aq}");

      return true;
    }

    public async Task<bool> DownloadAsync(Conversion conversion, Action<Conversion, long> progressAction, CancellationToken cancToken)
    {
      conversion.State = EConversionState.Downloading;
      using var logGuard = new LogGuard(3, this, () => conversion.ToString());

      try
      {
        if (conversion.DownloadUrl is null)
        {
          return false;
        }

        Uri requestUri = new Uri(conversion.DownloadUrl);
        var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        var response = await HttpClientAudible.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        string destfilename = (conversion.DownloadFileName + R.EncryptedFileExt).AsUncIfLong();
        long sourceFileSize = conversion.BookCommon.FileSizeBytes ?? 0;
        if (sourceFileSize == 0)
        {
          return false;
        }

        Log(3, this, () => $"{conversion}; size={sourceFileSize / (1024 * 1024)} MB");

        using var networkStream = await response.Content.ReadAsStreamAsync(cancToken);
        using var rdr = new BufferedStream(networkStream);
        using var fileStream = File.OpenWrite(destfilename);
        using var wrtr = new BufferedStream(fileStream);

        long accusize = await Task.Run(async () => await CopyStreams(conversion, rdr, wrtr, progressAction, cancToken), cancToken);

        bool succ = accusize >= sourceFileSize;
        if (!succ)
        {
          conversion.State = EConversionState.DownloadError;
        }
        else
        {
          BookLibrary.SavePersistentState(conversion, EConversionState.LocalLocked);
        }

        Log(3, this, () => $"{conversion}; download finished, succ={succ}.");
        return succ;
      }
      catch (Exception exc)
      {
        conversion.State = EConversionState.DownloadError;
        Log(1, this, () => $"{conversion}; {exc.Summary()}");
      }

      return false;
    }

    public async Task<bool> DecryptAsync(
      Conversion conversion,
      Action<Conversion, TimeSpan> progressAction,
      CancellationToken cancToken)
    {
      conversion.State = EConversionState.Unlocking;
      using var logGuard = new LogGuard(3, this, () => conversion.ToString());

      AaxFile aaxFile = null;
      var rg = new ResourceGuard(() => aaxFile?.Dispose());

      bool succ = false;
      int numChannels = 0;
      string inputFile = (conversion.DownloadFileName + R.EncryptedFileExt).AsUncIfLong();
      string outputFile = (conversion.DownloadFileName + R.DecryptedFileExt).AsUncIfLong();

      Mp4Operation operation = null;

      try
      {
        if (!File.Exists(inputFile))
        {
          return false;
        }

        using (var ifStream = File.OpenRead(inputFile))
        {
          aaxFile = new AaxFile(ifStream);
          aaxFile.SetDecryptionKey(conversion.BookCommon.LicenseKey, conversion.BookCommon.LicenseIv);

          numChannels = aaxFile.AudioChannels;

          using (var fileStream = File.OpenWrite(outputFile))
          {
            operation = aaxFile.ConvertToMp4aAsync(fileStream);
            operation.ConversionProgressUpdate += AaxFileConversionProgressUpdate;
            await operation;
            succ = operation.IsCompletedSuccessfully;
            if (succ)
            {
              progressAction.Invoke(conversion, TimeSpan.FromSeconds(conversion.BookMeta.RunTimeLengthSeconds ?? 0));
            }
          }
        }

        if (succ)
        {
          BookLibrary.SavePersistentState(conversion, EConversionState.LocalUnlocked);
        }
        else
        {
          conversion.State = EConversionState.UnlockingFailed;
        }

        Log(3, this, () => $"{conversion}; decryption finished, succ={succ}.");
      }
      catch (Exception exc)
      {
        conversion.State = EConversionState.UnlockingFailed;
        Log(1, this, () => $"{conversion}; {exc.Summary()}");
        return false;
      }

      if (succ && numChannels > 0)
      {
        try
        {
          string suffix = numChannels == 1 ? "_Mono" : "_Stereo";
          Rename(inputFile, suffix);
          Rename(outputFile, suffix);
          BookLibrary.SaveFileNameSuffix(conversion, suffix);
        }
        catch (Exception exc)
        {
          Log(1, this, () => $"{conversion}; {exc.Summary()}");
        }
      }

      return succ;

      void Rename(string file, string suffix)
      {
        string dir = Path.GetDirectoryName(file);
        string stub = Path.GetFileNameWithoutExtension(file);
        string ext = Path.GetExtension(file);
        string sfxfile = Path.Combine(dir, stub + suffix + ext);
        File.Move(file, sfxfile, true);
      }

      async void AaxFileConversionProgressUpdate(object sender, ConversionProgressEventArgs e)
      {
        if (cancToken.IsCancellationRequested)
        {
          await (operation?.CancelAsync() ?? Task.CompletedTask);
        }

        progressAction.Invoke(conversion, e.ProcessPosition);
      }
    }

    public async Task DownloadCoverImagesAsync()
    {
      using var logGuard = new LogGuard(3, this);
      await BookLibrary.AddCoverImagesAsync(url => HttpClientAmazon.DownloadImageAsync(url));
    }

    public async Task UpdateMetaInfo(IEnumerable<Component> components, Action<IEnumerable<Component>> onDone)
    {
      using var logGuard = new LogGuard(3, this, () => $"#comp={components.Count()}");
      var pairs = new List<ProductComponentPair>();
      foreach (var comp in components)
      {
        Log(3, this, () => comp.Conversion.ToString());
        var prod = await GetProductInfoAsync(comp.Asin);
        if (prod is null)
        {
          continue;
        }

        pairs.Add(new(prod, comp));
      }

      BookLibrary.UpdateComponentProduct(pairs);
      var result = pairs.Select(p => p.Component).ToList();
      onDone(result);
    }

    public async Task<Oahu.Audible.Json.Product> GetProductInfoAsync(string asin)
    {
      const string GROUPS
        = "response_groups=contributors,media,product_attrs,product_desc,product_extended_attrs," +
          "product_plan_details,product_plans,rating,review_attrs,reviews,sample,sku";

      string url = "/1.0/catalog/products/"
        + asin
        + "?"
        + GROUPS;

      string result = await CallAudibleApiSignedForStringAsync(url);

      if (Logging.Level >= 3)
      {
        string file = result.WriteTempJsonFile($"ProductResponse_{asin}");
        Log(3, this, () => $"asin={asin}, file=\"{Path.GetFileName(file)}\"");
      }

      Oahu.Audible.Json.ProductResponse productResponse = Oahu.Audible.Json.ProductResponse.Deserialize(result);

      Oahu.Audible.Json.Product product = productResponse?.Product;
#if TEST_INVAL_CHAR
      if (product is not null) {
        product.Title = product.Title.Replace (ORIG, SUBS);
      }
#endif

      return product;
    }

    public IEnumerable<Book> GetBooks()
    {
      return BookLibrary.GetBooks(new ProfileId(AccountId, Region));
    }

    public void SavePersistentState(Conversion conversion, EConversionState state)
    {
      BookLibrary.SavePersistentState(conversion, state);
    }

    public void RestorePersistentState(Conversion conversion)
    {
      BookLibrary.RestorePersistentState(conversion);
    }

    public EConversionState GetPersistentState(Conversion conversion)
    {
      return BookLibrary.GetPersistentState(conversion);
    }

    public void CheckUpdateFilesAndState(
      IDownloadSettings downloadSettings,
      IExportSettings exportSettings,
      Action<IConversion> callbackRefConversion,
      IInteractionCallback<InteractionMessage<BookLibInteract>, bool?> interactCallback)
    {
      BookLibrary.CheckUpdateFilesAndState(
        new ProfileId(AccountId, Region),
        downloadSettings,
        exportSettings,
        callbackRefConversion,
        interactCallback);
    }

    public int VerifyCompletedDownloads(
      IDownloadSettings downloadSettings,
      IExportSettings exportSettings)
    {
      return BookLibrary.VerifyCompletedDownloads(
        new ProfileId(AccountId, Region),
        downloadSettings,
        exportSettings);
    }

    internal async Task<Oahu.Audible.Json.LibraryResponse> GetLibraryAsync(string json, bool resync)
    {
      using var logGuard = new LogGuard(3, this, () => $"resync={resync}");

      const int PAGE_SIZE = 100;
      int page = 0;
      var libProducts = new List<Oahu.Audible.Json.Product>();

      if (json is null)
      {
        const string GROUPS
          = "response_groups=badge_types,category_ladders,claim_code_url,contributors,is_downloaded,is_returnable,media,"
          + "origin_asin,pdf_url,percent_complete,price,product_attrs,product_desc,product_extended_attrs,product_plan_details,"
          + "product_plans,provided_review,rating,relationships,review_attrs,reviews,sample,series,sku";

        DateTime dt = await BookLibrary.SinceLatestPurchaseDateAsync(new ProfileId(AccountId, Region), resync);

        while (true)
        {
          page++;
          string url = "/1.0/library"
            + $"?purchased_after={dt.ToXmlTime()}"
            + $"&num_results={PAGE_SIZE}"
            + $"&page={page}"
            + "&"
            + GROUPS;

          string pageResult = await CallAudibleApiSignedForStringAsync(url);
          if (pageResult is null)
          {
            return null;
          }

          if (Logging.Level >= 3)
          {
            string file = pageResult.WriteTempJsonFile("LibraryResponse");
            Log(3, this, () => $"page={page}, file=\"{Path.GetFileName(file)}\"");
          }

          Oahu.Audible.Json.LibraryResponse libraryResponse = Oahu.Audible.Json.LibraryResponse.Deserialize(pageResult);
          if (libraryResponse is null)
          {
            return null;
          }

          if (!(libraryResponse?.Items.Any() ?? false))
          {
            break;
          }

          var pageProducts = libraryResponse.Items;
#if TEST_UNAVAIL
          pageProducts = pageProducts.ToList().Take(pageProducts.Length - 1).ToArray();
#endif
          Log(3, this, () => $"#items/page={pageProducts.Length}");
          libProducts.AddRange(pageProducts);
        }
      }
      else
      {
        Oahu.Audible.Json.LibraryResponse libraryResponse = Oahu.Audible.Json.LibraryResponse.Deserialize(json);
        libProducts.AddRange(libraryResponse.Items);
      }

      libProducts = libProducts.DistinctBy(p => p.Asin).ToList();

      libProducts.Sort((x, y) => DateTime.Compare(x.PurchaseDate, y.PurchaseDate));

#if TEST_INVAL_CHAR
      libProducts = libProducts
        .Select (p => {
          p.Title = p.Title.Replace (ORIG, SUBS);
          return p;
        })
        .ToList ();
#endif

      await BookLibrary.AddRemBooksAsync(libProducts, new ProfileId(AccountId, Region), resync);

      var allPagesResponse = new Oahu.Audible.Json.LibraryResponse();
      allPagesResponse.Items = libProducts.ToArray();
      return allPagesResponse;
    }

    private static string BuildLicenseRequestBody(EDownloadQuality quality)
    {
      string json = $@"{{
        ""consumption_type"": ""Download"",
        ""supported_drm_types"": [""Adrm"", ""Mpeg""],
        ""quality"": ""{quality}"",
        ""response_groups"": ""last_position_heard,pdf_url,content_reference,chapter_info""
      }}";

      json = json.CompactJson();

      if (!json.ValidateJson())
      {
        throw new InvalidOperationException("invalid json");
      }

      return json;
    }

    private static async Task<long> CopyStreams(
      Conversion conversion,
      BufferedStream rdr,
      BufferedStream wrtr,
      Action<Conversion, long> progressAction,
      CancellationToken cancToken)
    {
      const int BUF_SIZE = 16384;
      long accusize = 0;
      byte[] buffer = new byte[BUF_SIZE];

      while (true)
      {
        if (cancToken.IsCancellationRequested)
        {
          return -1;
        }

        int size = await rdr.ReadAsync(buffer, 0, BUF_SIZE, cancToken);
        if (size == 0)
        {
          break;
        }

        accusize += size;
        progressAction(conversion, accusize);
        await wrtr.WriteAsync(buffer, 0, size, cancToken);
      }

      return accusize;
    }

    /// <summary>
    /// Imports a private key that may be PEM-encoded (PKCS#1 or PKCS#8) or raw base64 DER.
    /// Amazon's registration response sends the key with PEM headers and escaped newlines.
    /// </summary>
    private static void ImportPrivateKey(RSA rsa, string privateKey)
    {
      // First, try direct PEM import (works if the string has proper PEM headers and real newlines)
      try
      {
        rsa.ImportFromPem(privateKey);
        return;
      }
      catch (ArgumentException)
      {
        // PEM import failed — key may have escaped newlines or be raw base64
      }

      // Strip PEM headers and normalize newlines
      string cleaned = privateKey
        .Replace("-----BEGIN RSA PRIVATE KEY-----", string.Empty)
        .Replace("-----END RSA PRIVATE KEY-----", string.Empty)
        .Replace("-----BEGIN PRIVATE KEY-----", string.Empty)
        .Replace("-----END PRIVATE KEY-----", string.Empty)
        .Replace("\\n", string.Empty)
        .Replace("\n", string.Empty)
        .Replace("\r", string.Empty)
        .Trim();

      byte[] keyBytes = Convert.FromBase64String(cleaned);

      // Try PKCS#1 first (RSA PRIVATE KEY), then PKCS#8 (PRIVATE KEY)
      try
      {
        rsa.ImportRSAPrivateKey(keyBytes, out _);
      }
      catch (CryptographicException)
      {
        rsa.ImportPkcs8PrivateKey(keyBytes, out _);
      }
    }

    private void EnsureAccountId()
    {
      if (accountId > 0)
      {
        return;
      }

      var ctxt = Profile.GetAccountAliasContext(BookLibrary, GetAccountAliasFunc, false);

      accountId = ctxt.LocalId;
      accountAlias = ctxt.Alias;
    }

    private async Task<string> GetDownloadLicenseAsyncInternal(string asin, EDownloadQuality quality)
    {
      var url = $"{ContentPath}/{asin}/licenserequest";

      string jsonBody = BuildLicenseRequestBody(quality);

      return await CallAudibleApiSignedForStringAsync(url, jsonBody);
    }

    private void DecryptLicense(Oahu.Audible.Json.ContentLicense license)
    {
      // See also
      // https://patchwork.ffmpeg.org/project/ffmpeg/patch/17559601585196510@sas2-2fa759678732.qloud-c.yandex.net/
      if (license is null)
      {
        return;
      }

      string hashable = Profile.DeviceInfo.Type + Profile.DeviceInfo.Serial + Profile.CustomerInfo.AccountId +
        license.Asin;

      byte[] hashableBytes = Encoding.ASCII.GetBytes(hashable);
      byte[] key = new byte[16];
      byte[] iv = new byte[16];

      using var sha256 = SHA256.Create();
      byte[] hash = sha256.ComputeHash(hashableBytes);
      Array.Copy(hash, 0, key, 0, 16);
      Array.Copy(hash, 16, iv, 0, 16);

      byte[] encryptedText = Convert.FromBase64String(license.LicenseResponseText);

      using var aes = Aes.Create();
      aes.Mode = CipherMode.CBC;
      aes.Padding = PaddingMode.None;

      using var decryptor = aes.CreateDecryptor(key, iv);

      using var csDecrypt = new CryptoStream(new MemoryStream(encryptedText), decryptor, CryptoStreamMode.Read);

      csDecrypt.ReadExactly(encryptedText, 0, encryptedText.Length & 0x7ffffff0);

      string plainText = Encoding.ASCII.GetString(encryptedText.TakeWhile(b => b != 0).ToArray());

      Oahu.Audible.Json.Voucher voucher = Oahu.Audible.Json.Voucher.Deserialize(plainText);

      license.Voucher = voucher;
    }

    private async Task<string> CallAudibleApiSignedForStringAsync(string relUrl, string jsonBody = null)
    {
      HttpRequestMessage request = MakeSignedRequest(relUrl, jsonBody);
      return await SendForStringAsync(request, HttpClientAudible);
    }

    private async Task<byte[]> CallAudibleApiSignedForBytesAsync(string relUrl, string jsonBody = null)
    {
      HttpRequestMessage request = MakeSignedRequest(relUrl, jsonBody);
      return await SendForBytesAsync(request, HttpClientAudible);
    }

    private HttpRequestMessage MakeSignedRequest(string relUrl, string jsonBody)
    {
      Uri relUri = new Uri(relUrl, UriKind.Relative);

      var method = jsonBody is null ? HttpMethod.Get : HttpMethod.Post;

      var request = new HttpRequestMessage(method, relUri);
      request.Headers.Add("Accept", "application/json");

      if (jsonBody is not null)
      {
        HttpContent content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        request.Content = content;
      }

      SignRequest(request);
      return request;
    }

    private async Task<string> SendForStringAsync(HttpRequestMessage request, HttpClientEx httpClient)
    {
      string content = null;
      try
      {
        await request.LogAsync(4, this, httpClient.DefaultRequestHeaders, httpClient.CookieContainer, httpClient.BaseAddress);
        var response = await httpClient.SendAsync(request);
        await response.LogAsync(4, this, httpClient.CookieContainer, httpClient.BaseAddress);

        content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
          Log(1, this, () => $"API call failed: {(int)response.StatusCode} {response.ReasonPhrase} " +
            $"URL={request.RequestUri} Content={(content?.Length > 500 ? content[..500] : content)}");
        }

        response.EnsureSuccessStatusCode();

        return content;
      }
      catch (Exception exc)
      {
        Log(1, this, () => $"{exc.Summary()}{Environment.NewLine}{content}");
        return null;
      }
    }

    private async Task<byte[]> SendForBytesAsync(HttpRequestMessage request, HttpClientEx httpClient)
    {
      HttpResponseMessage response = null;
      try
      {
        await request.LogAsync(4, this, httpClient.DefaultRequestHeaders, httpClient.CookieContainer, httpClient.BaseAddress);
        response = await httpClient.SendAsync(request);
        await response.LogAsync(4, this, httpClient.CookieContainer, httpClient.BaseAddress);

        response.EnsureSuccessStatusCode();
        byte[] content = await response.Content.ReadAsByteArrayAsync();

        return content;
      }
      catch (Exception exc)
      {
        string content = await response.Content.ReadAsStringAsync();
        Log(1, this, () => $"{exc.Summary()}{Environment.NewLine}{content}");
        return null;
      }
    }

    private void SignRequest(HttpRequestMessage request)
    {
      string signature = MakeRequestSignature(request);

      request.Headers.Add("x-adp-token", Profile.AdpToken);
      request.Headers.Add("x-adp-alg", "SHA256withRSA:1.0");
      request.Headers.Add("x-adp-signature", signature);
    }

    private string MakeRequestSignature(HttpRequestMessage request)
    {
      DateTime dt = DateTime.UtcNow;

      string method = request.Method.ToString().ToUpper();
      string url = request.RequestUri.OriginalString;
      string time = dt.ToXmlTime();
      string content = request.Content?.ReadAsStringAsync().Result;
      string adpToken = Profile.AdpToken;

      string dataString = $"{method}\n{url}\n{time}\n{content}\n{adpToken}";

      byte[] signBytes = Sign(dataString);

      string encoded = Convert.ToBase64String(signBytes);
      var signature = $"{encoded}:{time}";

      return signature;
    }

    private byte[] Sign(string dataString)
    {
      byte[] dataBytes = Encoding.UTF8.GetBytes(dataString);

      using SHA256 sha256Hash = SHA256.Create();
      byte[] hashBytes = sha256Hash.ComputeHash(dataBytes);

      using RSA rsa = RSA.Create();
      ImportPrivateKey(rsa, Profile.PrivateKey);

      byte[] signatureBytes = rsa.SignHash(hashBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

      return signatureBytes;
    }
  }
}
