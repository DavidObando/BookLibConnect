using System.IO;

namespace Oahu.Decrypt.Mpeg4.Boxes;

public class MvexBox : Box
{
  public MvexBox(Stream file, BoxHeader header, IBox? parent) : base(header, parent)
  {
    LoadChildren(file);
  }
  protected override void Render(Stream file)
  {
    return;
  }
}
