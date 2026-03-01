using System.IO;
using Oahu.Decrypt.Mpeg4;

namespace Oahu.Decrypt
{
  public interface INewSplitCallback
  {
    Chapter Chapter { get; }

    int? TrackNumber { get; set; }

    int? TrackCount { get; set; }

    string? TrackTitle { get; set; }

    Stream? OutputFile { get; set; }
  }

  public interface INewSplitCallback<T> : INewSplitCallback where T : INewSplitCallback<T>
  {
    static abstract T Create(Chapter chapter);
  }

  public class NewSplitCallback : INewSplitCallback<NewSplitCallback>
  {
    private NewSplitCallback(Chapter chapter)
        => Chapter = chapter;

    public Chapter Chapter { get; }

    public int? TrackNumber { get; set; }

    public int? TrackCount { get; set; }

    public string? TrackTitle { get; set; }

    public Stream? OutputFile { get; set; }

    public static NewSplitCallback Create(Chapter chapter)
    {
      return new NewSplitCallback(chapter);
    }
  }
}
