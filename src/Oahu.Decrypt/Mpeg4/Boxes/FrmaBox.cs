using System.IO;
using Oahu.Decrypt.Mpeg4.Util;

namespace Oahu.Decrypt.Mpeg4.Boxes;

public class FrmaBox : Box
{
  public FrmaBox(Stream file, BoxHeader header, IBox? parent) : base(header, parent)
  {
    DataFormat = file.ReadType();
  }

  public override long RenderSize => base.RenderSize + 4;

  public string DataFormat { get; }

  protected override void Render(Stream file)
  {
    file.WriteType(DataFormat);
  }
}
