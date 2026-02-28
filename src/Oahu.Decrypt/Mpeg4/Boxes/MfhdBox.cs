using Oahu.Decrypt.Mpeg4.Util;
using System.IO;

namespace Oahu.Decrypt.Mpeg4.Boxes;

public class MfhdBox : FullBox
{
	public override long RenderSize => base.RenderSize + 4;
	public int SequenceNumber { get; }
	public MfhdBox(Stream file, BoxHeader header, IBox? parent) : base(file, header, parent)
	{
		SequenceNumber = file.ReadInt32BE();
	}
	protected override void Render(Stream file)
	{
		base.Render(file);
		file.WriteInt32BE(SequenceNumber);
	}
}
