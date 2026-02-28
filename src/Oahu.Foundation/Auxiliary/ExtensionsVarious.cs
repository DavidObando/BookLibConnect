using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Xml;

namespace Oahu.Aux.Extensions {
  public static class ExNullable {
    public static bool IsNullOrWhiteSpace (this string s) => string.IsNullOrWhiteSpace (s);
    public static bool IsNullOrEmpty (this string s) => string.IsNullOrEmpty (s);
    public static bool IsNullOrEmpty<T> (this IEnumerable<T> e) => e is null || e.Count() == 0;
    public static bool IsNull (this object o) => o is null;
  }

  public static class ExEnumerable {
    public static void ForEach<T> (this IEnumerable<T> items, Action<T> action) {
      foreach (T item in items)
        action (item);
    }
  }

  public static class ExString {
    public const string SEPARATOR = "; ";
    public const char ELLIPSIS = '…';

    public static string FirstEtAl (this IEnumerable<string> values, char separator) =>
      values.firstEtAl ($"{separator} ");

    public static string FirstEtAl (this IEnumerable<string> values, string separator = SEPARATOR) =>
      values.firstEtAl (separator);

    public static string Combine (this IEnumerable<string> values, char separator) =>
      values.combine (false, $"{separator} ");

    public static string Combine (this IEnumerable<string> values, string separator = SEPARATOR) =>
      values.combine (false, separator);

    public static string Combine (this IEnumerable<string> values, bool newLine) =>
      values.combine (newLine, SEPARATOR);

    private static string firstEtAl (this IEnumerable<string> values, string separator) {
      if (values.IsNullOrEmpty())
        return null;
      if (values.Count () > 1)
        return $"{values.First ()}{separator}{ELLIPSIS}";
      else
        return values.First ();
    }

    private static string combine (this IEnumerable<string> values, bool newLine, string separator) {
      if (values is null)
        return null;
      var sb = new StringBuilder ();
      foreach (string v in values) {
        if (string.IsNullOrWhiteSpace (v))
          continue;
        if (sb.Length > 0) {
          sb.Append (separator);
          if (newLine)
            sb.AppendLine ();
        }
        sb.Append (v);
      }
      return sb.ToString ();
    }

    public static string[] SplitTrim (this string value, char separator) => value.SplitTrim (new[] { separator });

    public static string[] SplitTrim (this string value, char[] separators = null) {
      if (string.IsNullOrWhiteSpace (value))
        return new string[0];
      if (separators is null)
        separators = new[] { ',', ';' };

      var values = value.Split (separators);
      values = values.Select (v => v.Trim ()).ToArray ();
      return values;
    }

    static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars ();
    static readonly char[] DoubtfulFileNameChars = {
      '¡', '¢', '£', '¤', '¥', '¦', '§', '¨', '©', 'ª', '«', '¬', '®', '¯', '°', '±',
      '²', '³', '´', 'µ', '¶', '·', '¸', '¹', 'º', '»', '¼', '½', '¾', '¿', '×', '÷',
      '‘', '’', 'ƒ', '„', '…', '†', '‡', 'ˆ', '‰', '‹', '’', '“', '”', '•', '–', '—',
      '˜', '™', '›'
    };

    public static string Prune (this string s, char[] invalid) {
      char[] doubtful = null;
      if (s is null)
        return null;
      if (invalid is null) {
        invalid = InvalidFileNameChars;
        doubtful = DoubtfulFileNameChars;
      }
      StringBuilder sb = new StringBuilder ();
      foreach (char c in s) {
        if (invalid.Contains (c))
          continue;
        //sb.Append (',');
        else if (doubtful?.Contains (c) ?? false)
          continue;
        else
          sb.Append (c);
      }
      return sb.ToString ();
    }

    public static string Prune (this string s) {
      if (s is null)
        return null;
      string pruned = s.Prune (null);
      pruned = pruned.Trim ('.');
      return pruned;
    }

    public static string SubstitUser (this string s) {
      if (s is null)
        return null;
      string userdir = ApplEnv.UserDirectoryRoot;
      if (!s.Contains (userdir))
        return s;
      string userdir1 = userdir.Replace (ApplEnv.UserName, "USER");
      string s1 = s.Replace (userdir, userdir1);
      return s1;
    }

    const int MAXLEN_SHORTSTRING = 40;

    /// <summary>
    /// Performs the ROT13 character rotation.
    /// </summary>
    public static string Rot13 (this string value) {
      const int C = 13;
      char[] array = value.ToCharArray ();
      for (int i = 0; i < array.Length; i++) {
        int number = (int)array[i];

        if (number >= 'a' && number <= 'z') {
          if (number > 'm') {
            number -= C;
          } else {
            number += C;
          }
        } else if (number >= 'A' && number <= 'Z') {
          if (number > 'M') {
            number -= C;
          } else {
            number += C;
          }
        }
        array[i] = (char)number;
      }
      return new string (array);
    }
  }

  public static class ExEncoding {
    // TODO implement encoding param
    public static byte[] GetBytes (this string s, Encoding enc = null) => Encoding.ASCII.GetBytes (s);
  }

  public static class JsonExtensions {
    public static JsonSerializerOptions Options { get; } = new JsonSerializerOptions {
      TypeInfoResolver = new DefaultJsonTypeInfoResolver (),
      WriteIndented = true,
      ReadCommentHandling = JsonCommentHandling.Skip,
      AllowTrailingCommas = true,
      Converters ={
        new JsonStringEnumConverter()
      },
      Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string SerializeToJsonAny (this object any) {
      try {
        string result = JsonSerializer.Serialize (any, any.GetType (), Options);
        return result;
      } catch (Exception) {
        return null;
      }
    }


    public static T DeserializeJson<T> (this string json) {
      try {
        T result = JsonSerializer.Deserialize<T> (json, Options);
        return result;
      } catch (Exception) {
        return default (T);
      }
    }
  }

  public static class ExDateTime {
    public static DateTime RoundDown (this DateTime date, TimeSpan span) {
      long ticks = date.Ticks / span.Ticks;
      return new DateTime (ticks * span.Ticks, date.Kind);
    }
    public static string ToXmlTime (this DateTime dt) =>
      XmlConvert.ToString (dt, XmlDateTimeSerializationMode.Utc);

  }

  public static class ExUnc {
    private const string UNC = @"UNC\";
    private const string UNC_PFX = @"\\?\";
    private const string UNC_NET = UNC_PFX + UNC;

    public static bool IsUnc (this string path) {
      string root = Path.GetPathRoot (path);

      if (root.StartsWith (UNC_PFX))
        return true;

      return false;
    }

    public static string AsUncIfLong (this string path) {
      if (path.IsUnc ())
        return path;
      path = Path.GetFullPath (path);
      if (path.Length < 250)
        return path;
      return path.AsUnc ();
    }

    public static string AsUnc (this string path) {
      if (path.IsUnc ())
        return path;
      else {
        string root = Path.GetPathRoot (path);

        if (root.StartsWith (@"\\")) {
          string s = path.Substring (2);
          return UNC_NET + s;
        } else
          return UNC_PFX + path;
      }
    }

  }

  public static class ExHex {
    public static string BytesToHexString (this byte[] ba) {
      if (ba is null)
        return null;
      return BitConverter.ToString (ba).Replace ("-", "").ToLower ();
    }
  }

  public static class ExFile {
    private static readonly TimeSpan ONE_MS = TimeSpan.FromMilliseconds (1);

    public static string GetUniqueTimeBasedFilename (this string path, bool alwaysUseSpaceSep = false) {
      const char SPC = ' ';
      const char DSH = '-';

      string dir = Path.GetDirectoryName (path);
      string filnamstub = Path.GetFileNameWithoutExtension (path);
      string ext = Path.GetExtension (path);

      char c = (alwaysUseSpaceSep || filnamstub.Contains (SPC)) ? SPC : DSH;
      string fmt1 = $"{c}yyyy_MM_dd{c}HH_mm_ss";
      string fmt2 = $"{fmt1}_fff";
      string fmt = fmt1;

      string result;

      DateTime timestamp = DateTime.Now;
      while (true) {
        string sTimestamp = timestamp.ToString (fmt);
        result = Path.Combine (dir, filnamstub + sTimestamp + ext);
        if (!File.Exists (result))
          break;
        timestamp += ONE_MS;
        fmt = fmt2;
      }

      return result;
    }
  }
  public static class ExBase64 {
    public static string ToBase64StringTrimmed (this byte [] bytes) =>
      bytes.ToBase64String ().TrimBase64String ();

    public static string ToBase64String (this byte [] bytes) =>
      Convert.ToBase64String (bytes);

    public static string ToUrlBase64String (this byte[] bytes) =>
      bytes.ToBase64StringTrimmed ().Replace ('+', '-').Replace ('/', '_');

    public static string TrimBase64String (this string s) =>
      s.TrimEnd ('=');

    public static byte[] FromBase64String (this string s) {
      s = s.Trim ();
      int n = s.Length % 4;
      string padded = n switch {
        2 => s + "==",
        3 => s + "=",
        _ => s,
      };
      try {
        return Convert.FromBase64String (padded);
      } catch (Exception) {
        return null;
      }
    }

  }

  public static class ExImage {
    private static readonly byte[] __jpegHeader = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] __pngHeader = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly byte[] __gifHeader = { 0x47, 0x49, 0x46 };
    private static readonly byte[] __bmpHeader = { 0x42, 0x4D };
    private static readonly byte[] __tiffLe = { 0x49, 0x49, 0x2A, 0x00 };
    private static readonly byte[] __tiffBe = { 0x4D, 0x4D, 0x00, 0x2A };

    public static string FindImageFormat (this byte[] bytes) {
      if (bytes is null || bytes.Length < 8)
        return null;
      try {
        if (startsWith (bytes, __jpegHeader))
          return ".jpg";
        if (startsWith (bytes, __pngHeader))
          return ".png";
        if (startsWith (bytes, __gifHeader))
          return ".gif";
        if (startsWith (bytes, __bmpHeader))
          return ".bmp";
        if (startsWith (bytes, __tiffLe) || startsWith (bytes, __tiffBe))
          return ".tif";
        return null;
      } catch (Exception) {
        return null;
      }
    }

    private static bool startsWith (byte[] data, byte[] signature) {
      if (data.Length < signature.Length)
        return false;
      for (int i = 0; i < signature.Length; i++) {
        if (data[i] != signature[i])
          return false;
      }
      return true;
    }

  }

  public static class ExException {
    public static string Summary (this Exception exc, bool withCRLF = false) =>
      $"{exc.GetType ().Name}:{(withCRLF ? Environment.NewLine : " ")}\"{exc.Message.SubstitUser()}\"";
  }

  public static class ExType {
    public static string PrettyName (this Type type, int? level = null, bool fullName = false) {
      int nargs = type.GetGenericArguments ().Length;
      if (nargs == 0 || (level.HasValue && nargs > level.Value))
        return typeName ();
      var genericArguments = type.GetGenericArguments ();
      var typeDefinition = type.Name;
      int idx = typeDefinition.IndexOf ("`");
      if (idx < 0)
        return typeName ();
      var unmangledName = typeDefinition.Substring (0, idx);
      return unmangledName + $"<{string.Join (",", genericArguments.Select (t => t.PrettyName(1)))}>";

      string typeName () => fullName ? type.FullName : type.Name;

    }
  }
}
