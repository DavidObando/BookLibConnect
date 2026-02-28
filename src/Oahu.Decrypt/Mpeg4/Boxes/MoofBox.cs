using System.IO;

namespace Oahu.Decrypt.Mpeg4.Boxes;

public class MoofBox : Box
{
  public MoofBox(Stream file, BoxHeader header) : base(header, null)
  {
    LoadChildren(file);
  }

  public MfhdBox Mfhd => GetChildOrThrow<MfhdBox>();

  public TrafBox Traf => GetChildOrThrow<TrafBox>();

  protected override void Render(Stream file)
  {
    return;
  }
}
