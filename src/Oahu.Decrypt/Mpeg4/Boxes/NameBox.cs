using System.Diagnostics;
using System.IO;
using System.Text;
using Oahu.Decrypt.Mpeg4.Util;

namespace Oahu.Decrypt.Mpeg4.Boxes
{
  [DebuggerDisplay("{DebuggerDisplay,nq}")]
  public class NameBox : FullBox
  {
    public NameBox(Stream file, BoxHeader header, IBox? parent) : base(file, header, parent)
    {
      var stringSize = RemainingBoxLength(file);
      var stringData = file.ReadBlock((int)stringSize);
      Name = Encoding.UTF8.GetString(stringData);
    }

    private NameBox(BoxHeader header, IBox? parent, string name)
        : base(new byte[4], header, parent)
    {
      Name = name;
    }

    public override long RenderSize => base.RenderSize + Encoding.UTF8.GetByteCount(Name);

    public string Name { get; set; }

    [DebuggerHidden]
    private string DebuggerDisplay => $"name: {Name}";

    public static NameBox Create(IBox? parent, string name)
    {
      int size = Encoding.UTF8.GetByteCount(name) + 12 /* empty FullBox size*/;
      BoxHeader header = new BoxHeader((uint)size, "name");

      NameBox nameBox = new NameBox(header, parent, name);

      parent?.Children.Add(nameBox);
      return nameBox;
    }

    protected override void Render(Stream file)
    {
      base.Render(file);
      file.Write(Encoding.UTF8.GetBytes(Name));
    }
  }
}
