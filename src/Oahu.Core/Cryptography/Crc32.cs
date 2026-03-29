namespace Oahu.Core.Cryptography;

internal static class Crc32
{
  private const uint Polynomial = 3988292384;
  private static readonly uint[] Table = new uint[256];

  static Crc32()
  {
    uint value, temp;
    for (uint i = 0; i < Table.Length; ++i)
    {
      value = 0;
      temp = i;
      for (byte j = 0; j < 8; ++j)
      {
        if (((value ^ temp) & 0x1) != 0)
        {
          value = value >> 1 ^ Polynomial;
        }
        else
        {
          value >>= 1;
        }

        temp >>= 1;
      }

      Table[i] = value;
    }
  }

  public static uint ComputeChecksum(byte[] bytes)
  {
    uint crc = 0;
    crc ^= uint.MaxValue;
    for (int i = 0; i < bytes.Length; ++i)
    {
      byte index = (byte)(crc ^ bytes[i]);
      crc = crc >> 8 ^ Table[index];
    }

    crc ^= uint.MaxValue;
    return crc;
  }
}
