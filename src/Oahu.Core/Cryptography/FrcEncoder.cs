using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Oahu.Core.Cryptography;

internal static class FrcEncoder
{
  public static string Encode(string deviceSn, string json)
  {
    var compressed = GzipCompress(Encoding.UTF8.GetBytes(json));
    var key = deviceSn.AsSpan();
    ReadOnlySpan<byte> iv = RandomNumberGenerator.GetBytes(16);

    var encrypted = EncryptFrc(key, iv, compressed);
    var sig = ComputeSig(key, iv, encrypted);
    var bytes = new byte[1 + sig.Length + iv.Length + encrypted.Length];
    sig.CopyTo(bytes.AsSpan(1));
    iv.CopyTo(bytes.AsSpan(1 + sig.Length));
    encrypted.CopyTo(bytes.AsSpan(1 + sig.Length + iv.Length));

    return Convert.ToBase64String(bytes);
  }

  private static byte[] GzipCompress(byte[] data)
  {
    using var ms = new MemoryStream();
    using (var gzip = new GZipStream(ms, CompressionLevel.SmallestSize))
      gzip.Write(data);
    return ms.ToArray();
  }

  private static byte[] EncryptFrc(ReadOnlySpan<char> deviceSn, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> data)
  {
    using var aes = GetAes(deviceSn);
    return aes.EncryptCbc(data, iv, PaddingMode.PKCS7);
  }

  private static byte[] ComputeSig(ReadOnlySpan<char> deviceSn, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> data)
  {
    var key = GetKeyFromPassword(deviceSn, "HmacSHA256"u8);
    using var hmac = new HMACSHA256(key);
    var bytes = new byte[iv.Length + data.Length];
    iv.CopyTo(bytes);
    data.CopyTo(bytes.AsSpan(iv.Length));
    return hmac.ComputeHash(bytes)[..8];
  }

  private static Aes GetAes(ReadOnlySpan<char> deviceSn)
  {
    var aes = Aes.Create();
    aes.Key = GetKeyFromPassword(deviceSn, "AES/CBC/PKCS7Padding"u8);
    return aes;
  }

  private static byte[] GetKeyFromPassword(ReadOnlySpan<char> deviceSn, ReadOnlySpan<byte> salt)
    => Rfc2898DeriveBytes.Pbkdf2(deviceSn, salt, 1000, HashAlgorithmName.SHA1, 16);
}
