using Oahu.Decrypt.Mpeg4.Util;

namespace Oahu.Decrypt.Mpeg4.Boxes.AC4SpecificBox;

/// <summary>
/// ETSI TS 103 190-2 E.7 ac4_bitrate_dsi
/// </summary>
public class ac4_bitrate_dsi
{
  public BitRateMode bit_rate_mode;
  public uint bit_rate;
  public uint bit_rate_precision;

  public ac4_bitrate_dsi(BitReader reader)
  {
    bit_rate_mode = (BitRateMode)reader.Read(2);
    bit_rate = reader.Read(32);
    bit_rate_precision = reader.Read(32);
  }
}
