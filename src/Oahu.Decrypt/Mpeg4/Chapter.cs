using System;
using System.IO;
using System.Text;
using Oahu.Decrypt.Mpeg4.Util;

namespace Oahu.Decrypt.Mpeg4;

public record Chapter
{
  // This is constant folr UTF-8 text
  // https://github.com/FFmpeg/FFmpeg/blob/master/libavformat/movenc.c
  private static readonly byte[] encd = [0, 0, 0, 0xc, (byte)'e', (byte)'n', (byte)'c', (byte)'d', 0, 0, 1, 0];

  public Chapter(string title, TimeSpan start, TimeSpan duration)
  {
    ArgumentNullException.ThrowIfNull(title, nameof(title));
    Title = title;
    StartOffset = start;
    Duration = duration;
    EndOffset = StartOffset + Duration;
  }

  public string Title { get; }

  public TimeSpan StartOffset { get; }

  public TimeSpan Duration { get; }

  public TimeSpan EndOffset { get; }

  public int RenderSize => 2 + Encoding.UTF8.GetByteCount(Title) + encd.Length;

  public void WriteChapter(Stream output)
  {
    byte[] title = Encoding.UTF8.GetBytes(Title);

    output.WriteInt16BE((short)title.Length);
    output.Write(title);
    output.Write(encd);
  }

  public override string ToString()
  {
    return $"{Title} {{{StartOffset} - {EndOffset}}}";
  }
}
