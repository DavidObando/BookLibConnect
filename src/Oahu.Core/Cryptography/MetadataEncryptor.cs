using System;
using System.Text;

namespace Oahu.Core.Cryptography;

internal static class MetadataEncryptor
{
  private static readonly uint[] AmazonKey = [4169969034, 4087877101, 1706678977, 3681020276];

  public static string Encrypt(string metadata)
  {
    ArgumentNullException.ThrowIfNull(metadata);

    var metadataBytes = Encoding.ASCII.GetBytes(metadata);
    var crc = Crc32.ComputeChecksum(metadataBytes).ToString("X8");
    var clearString = crc + "#" + metadata;
    var clearBytes = Encoding.ASCII.GetBytes(clearString);
    var cipherBytes = XXTEA.Encrypt(clearBytes, AmazonKey);

    return "ECdITeCs:" + Convert.ToBase64String(cipherBytes);
  }
}
