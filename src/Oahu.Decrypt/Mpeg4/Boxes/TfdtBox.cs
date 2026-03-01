using System.IO;
using Oahu.Decrypt.Mpeg4.Util;

namespace Oahu.Decrypt.Mpeg4.Boxes;

public class TfdtBox : FullBox
{
  public TfdtBox(Stream file, BoxHeader header, IBox? parent) : base(file, header, parent)
  {
    BaseMediaDecodeTime = Version == 1 ? file.ReadInt64BE() : file.ReadUInt32BE();
  }

  public override long RenderSize => base.RenderSize + (Version == 1 ? 8 : 4);

  public long BaseMediaDecodeTime { get; }

  protected override void Render(Stream file)
  {
    base.Render(file);
    if (Version == 1)
    {
      file.WriteInt64BE(BaseMediaDecodeTime);
    }
    else
    {
      file.WriteUInt32BE((uint)BaseMediaDecodeTime);
    }
  }
}
