using Oahu.Decrypt.Mpeg4.Chunks;
using System;

namespace Oahu.Decrypt.FrameFilters;

public class FrameEntry
{
	public ChunkEntry? Chunk { get; init; }
	public required uint SamplesInFrame { get; init; }
	public required Memory<byte> FrameData { get; init; }
	public object? ExtraData { get; set; }
}
