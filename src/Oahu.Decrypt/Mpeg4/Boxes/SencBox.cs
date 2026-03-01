using System;
using System.IO;
using System.Linq;
using Oahu.Decrypt.Mpeg4.Util;

namespace Oahu.Decrypt.Mpeg4.Boxes;

public class SencBox : FullBox
{
  public SencBox(Stream file, BoxHeader header, IBox? parent) : base(file, header, parent)
  {
    var sampleCount = file.ReadInt32BE();

    if (UseSubSampleEncryption)
    {
      throw new NotSupportedException(nameof(UseSubSampleEncryption));
    }

    var ivSize = (int)((header.TotalBoxSize - 16) / sampleCount);

    IVs = new byte[sampleCount][];

    for (int i = 0; i < sampleCount; i++)
    {
      IVs[i] = file.ReadBlock(ivSize);
    }
  }

  public override long RenderSize => base.RenderSize + 4 + IVs.Sum(iv => iv.Length);

  public bool UseSubSampleEncryption => (Flags & 2) == 2;

  public byte[][] IVs { get; }

  protected override void Render(Stream file)
  {
    base.Render(file);
    file.WriteInt32BE(IVs.Length);
    foreach (var iv in IVs)
    {
      file.Write(iv);
    }
  }
}
