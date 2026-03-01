using System.Diagnostics;
using System.IO;
using System.Text;
using Oahu.Decrypt.Mpeg4.Util;

namespace Oahu.Decrypt.Mpeg4.Boxes;

[DebuggerDisplay("{DebuggerDisplay,nq}")]
public class MeanBox : FullBox
{
  public MeanBox(Stream file, BoxHeader header, IBox? parent) : base(file, header, parent)
  {
    var stringSize = RemainingBoxLength(file);
    var stringData = file.ReadBlock((int)stringSize);
    ReverseDnsDomain = Encoding.UTF8.GetString(stringData);
  }

  private MeanBox(BoxHeader header, IBox? parent, string domain)
      : base(new byte[4], header, parent)
  {
    ReverseDnsDomain = domain;
  }

  public override long RenderSize => base.RenderSize + Encoding.UTF8.GetByteCount(ReverseDnsDomain);

  public string ReverseDnsDomain { get; set; }

  private string DebuggerDisplay => $"domain: {ReverseDnsDomain}";

  public static MeanBox Create(IBox? parent, string domain)
  {
    int size = Encoding.UTF8.GetByteCount(domain) + 12 /* empty FullBox size*/;
    BoxHeader header = new BoxHeader((uint)size, "mean");

    MeanBox meanBox = new MeanBox(header, parent, domain);

    parent?.Children.Add(meanBox);
    return meanBox;
  }

  protected override void Render(Stream file)
  {
    base.Render(file);
    file.Write(Encoding.UTF8.GetBytes(ReverseDnsDomain));
  }
}
