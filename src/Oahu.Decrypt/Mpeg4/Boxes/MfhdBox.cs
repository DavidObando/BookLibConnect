using System.IO;
using Oahu.Decrypt.Mpeg4.Util;

namespace Oahu.Decrypt.Mpeg4.Boxes;

public class MfhdBox : FullBox
{
  public MfhdBox(Stream file, BoxHeader header, IBox? parent) : base(file, header, parent)
  {
    SequenceNumber = file.ReadInt32BE();
  }

  public override long RenderSize => base.RenderSize + 4;

  public int SequenceNumber { get; }

  protected override void Render(Stream file)
  {
    base.Render(file);
    file.WriteInt32BE(SequenceNumber);
  }
}
