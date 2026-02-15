using System;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace BookLibConnect.Aux {
  public static class ApplEnv {

    static readonly char[] INVALID_CHARS = Path.GetInvalidFileNameChars ();

    public static Version OSVersion { get; } = getOSVersion();
    public static bool Is64BitOperatingSystem => Environment.Is64BitOperatingSystem;
    public static bool Is64BitProcess => Environment.Is64BitProcess;
    public static int ProcessorCount => Environment.ProcessorCount;

    public static Assembly EntryAssembly { get; } = Assembly.GetEntryAssembly ();
    public static Assembly ExecutingAssembly { get; } = Assembly.GetExecutingAssembly ();
    public static string AssemblyVersion { get; } = ThisAssembly.AssemblyFileVersion;
    public static string AssemblyTitle { get; } = 
      getAttribute<AssemblyTitleAttribute> ()?.Title ?? Path.GetFileNameWithoutExtension (ExecutingAssembly.Location);
    public static string AssemblyProduct { get; } = getAttribute<AssemblyProductAttribute> ()?.Product;
    public static string AssemblyCopyright { get; } = getAttribute<AssemblyCopyrightAttribute> ()?.Copyright;
    public static string AssemblyCompany { get; } = getAttribute<AssemblyCompanyAttribute> ()?.Company;
    public static string NeutralCultureName { get; } = getAttribute<NeutralResourcesLanguageAttribute> ()?.CultureName;

    public static string AssemblyGuid { get; } = getAttribute<GuidAttribute> ()?.Value;  

    public static string ApplName { get; } = EntryAssembly.GetName ().Name;
    public static string ApplDirectory { get; } = AppContext.BaseDirectory;
    public static string LocalDirectoryRoot { get; } = Environment.GetFolderPath (Environment.SpecialFolder.LocalApplicationData);
    public static string LocalApplDirectory { get; } = Path.Combine (LocalDirectoryRoot, ApplName);
    public static string SettingsDirectory { get; } = Path.Combine (LocalApplDirectory, "settings");
    public static string TempDirectory { get; } = Path.Combine (LocalApplDirectory, "tmp");
    public static string LogDirectory { get; } = Path.Combine (LocalApplDirectory, "log");
    public static string UserName { get; } = Environment.UserName;
    public static string UserDirectoryRoot { get; } = Environment.GetFolderPath (Environment.SpecialFolder.UserProfile);

    private static T getAttribute<T> () where T : Attribute {
      object[] attributes = EntryAssembly.GetCustomAttributes (typeof (T), false);
      if (attributes.Length == 0)
        return null;
      return attributes[0] as T;
    }

    private static Version getOSVersion () {
      const string REGEX = @"\s([0-9.]+)";
      string os = RuntimeInformation.OSDescription;
      var regex = new Regex (REGEX);
      var match = regex.Match (os);
      if (!match.Success)
        return new Version ();
      string osvers = match.Groups[1].Value;
      try {
        return new Version (osvers);
      } catch (Exception) {
        return new Version ();
      }
    }
  }
}
