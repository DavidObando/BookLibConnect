
using Oahu.BooksDatabase;

namespace Oahu.Core {
  public enum EDownloadQualityReducedChoices {
    Normal,
    High,
  }

  public static class DownloadQualityExtensions {
    public static EDownloadQuality ToFullChoices (this EDownloadQualityReducedChoices value) {
      return value switch {
        EDownloadQualityReducedChoices.High => EDownloadQuality.High,
        _ => EDownloadQuality.Normal
      };
    }
    public static EDownloadQualityReducedChoices ToReducedChoices (this EDownloadQuality value) {
      return value switch {
        EDownloadQuality.Extreme => EDownloadQualityReducedChoices.High,
        EDownloadQuality.High => EDownloadQualityReducedChoices.High,
        _ => EDownloadQualityReducedChoices.Normal
      };
    }

  }
}
