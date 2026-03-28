namespace Oahu.BooksDatabase
{
  public enum ECodec
  {
    Format4,
    Mp42232,
    Mp42264,
    Mp44464,
    Mp444128,
    Aax,
    Aax2232,
    Aax2264,
    Aax4464,
    Aax44128,
  }

  public enum EDeliveryType
  {
    SinglePartBook,
    MultiPartBook,
    AudioPart,
    BookSeries,
    Periodical
  }

  public enum EConversionState
  {
    Unknown,          // strikethru globe
    Remote,           // globe
    Download,         // globe with down arrow
    LicenseGranted,   // key
    LicenseDenied,    // strikethru key
    Downloading,      // down arrow
    DownloadError,    // strikethru down arrow
    LocalLocked,      // lock closed
    Unlocking,        // key over lock
    UnlockingFailed,  // strikethru key over lock
    LocalUnlocked,    // lock open
    Exported,         // checkmark mauve
    Converting,       // right arrow
    Converted,        // checkmark green
    ConvertedUnknown, // checkmark gray
    ConversionError,  // strikethru right arrow
  }

  public enum ELicenseStatusCode
  {
    Unknown,
    Granted
  }

  public enum EDownloadQuality
  {
    Low,
    Normal,
    High,
    Extreme
  }

  public enum EPseudoAsinId
  {
    None,
    Author,
    Narrator,
  }
}
