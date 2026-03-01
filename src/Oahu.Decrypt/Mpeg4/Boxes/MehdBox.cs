using System.IO;
using Oahu.Decrypt.Mpeg4.Util;

namespace Oahu.Decrypt.Mpeg4.Boxes;

public class MehdBox : FullBox
{
  public MehdBox(Stream file, BoxHeader header, IBox? parent) : base(file, header, parent)
  {
    FragmentDuration = Version == 1 ? file.ReadUInt64BE() : file.ReadUInt32BE();
  }

  public override long RenderSize => base.RenderSize + (Version == 1 ? 8 : 4);

  public ulong FragmentDuration { get; }

  protected override void Render(Stream file)
  {
    base.Render(file);
    if (Version == 1)
    {
      file.WriteUInt64BE(FragmentDuration);
    }
    else
    {
      file.WriteUInt32BE((uint)FragmentDuration);
    }
  }
}
