using System.IO;
using Oahu.Decrypt.Mpeg4.Util;

namespace Oahu.Decrypt.Mpeg4.Boxes;

public class AdrmBox : Box
{
  private readonly byte[] beginBlob;
  private readonly byte[] middleBlob;
  private readonly byte[] endBlob;

  public AdrmBox(Stream file, BoxHeader header, IBox? parent) : base(header, parent)
  {
    beginBlob = file.ReadBlock(8);
    DrmBlob = file.ReadBlock(56);
    middleBlob = file.ReadBlock(4);
    Checksum = file.ReadBlock(20);
    long len = RemainingBoxLength(file);
    endBlob = file.ReadBlock((int)len);
  }

  public override long RenderSize => base.RenderSize + beginBlob.Length + DrmBlob.Length + middleBlob.Length + Checksum.Length + endBlob.Length;

  public byte[] DrmBlob { get; }

  public byte[] Checksum { get; }

  protected override void Render(Stream file)
  {
    file.Write(beginBlob);
    file.Write(DrmBlob);
    file.Write(middleBlob);
    file.Write(Checksum);
    file.Write(endBlob);
  }
}
