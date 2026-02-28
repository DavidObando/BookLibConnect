using System;

namespace Oahu.CommonTypes
{
  public interface IBookMeta : IAudioQuality
  {
    string Asin { get; }
    string Title { get; }
    string Author { get; }
    string MultiAuthors { get; }
    long? FileSizeBytes { get; }
    int? RunTimeLengthSeconds { get; }
    string Narrator { get; }
    string MultiNarrators { get; }
    string Sku { get; }
    string SkuLite { get; }
    DateTime? ReleaseDate { get; }
    DateTime? PurchaseDate { get; }
  }

  public interface IAudioQuality
  {
    int? SampleRate { get; }
    int? BitRate { get; }
  }
}
