using Oahu.Aux.Extensions;
using Oahu.CommonTypes;

namespace Oahu.BooksDatabase {
  public record AudioQuality (int? SampleRate, int? BitRate) : IAudioQuality;

  namespace ex {
    public static class ExCodec {

      public static AudioQuality ToQuality (this Codec codec) => codec.Name.ToQuality ();

      public static AudioQuality ToQuality(this ECodec codec) {
        string name = codec.ToString ();
        if (!name.StartsWith ("aax"))
          return default;

        var parts = name.SplitTrim ('_');
        if (parts.Length < 3)
          return default;

        var qual = new AudioQuality (int.Parse (parts[1]), int.Parse (parts[2]));
        qual = qual with {
          SampleRate = qual.SampleRate * 22050 / 22
        };

        return qual;
      }
    }
  }
}
