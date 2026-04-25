using System;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Oahu.Aux
{
  public static class ApplEnv
  {
    static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();

    public static Version OSVersion { get; } = GetOsVersion();

    public static bool Is64BitOperatingSystem => Environment.Is64BitOperatingSystem;

    public static bool Is64BitProcess => Environment.Is64BitProcess;

    public static int ProcessorCount => Environment.ProcessorCount;

    public static Assembly EntryAssembly { get; } = Assembly.GetEntryAssembly();

    public static Assembly ExecutingAssembly { get; } = Assembly.GetExecutingAssembly();

    public static string AssemblyVersion { get; } = ThisAssembly.AssemblyFileVersion;

    public static string AssemblyTitle { get; } =
      GetAttribute<AssemblyTitleAttribute>()?.Title ?? Path.GetFileNameWithoutExtension(ExecutingAssembly.Location);

    public static string AssemblyProduct { get; } = GetAttribute<AssemblyProductAttribute>()?.Product;

    public static string AssemblyCopyright { get; } = GetAttribute<AssemblyCopyrightAttribute>()?.Copyright;

    public static string AssemblyCompany { get; } = GetAttribute<AssemblyCompanyAttribute>()?.Company;

    public static string NeutralCultureName { get; } = GetAttribute<NeutralResourcesLanguageAttribute>()?.CultureName;

    public static string AssemblyGuid { get; } = GetAttribute<GuidAttribute>()?.Value;

    public static string ApplName { get; private set; } = EntryAssembly.GetName().Name;

    public static string ApplDirectory { get; } = AppContext.BaseDirectory;

    public static string LocalDirectoryRoot { get; } = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string LocalApplDirectory => Path.Combine(LocalDirectoryRoot, ApplName);

    public static string SettingsDirectory => Path.Combine(LocalApplDirectory, "settings");

    public static string TempDirectory => Path.Combine(LocalApplDirectory, "tmp");

    public static string LogDirectory => Path.Combine(LocalApplDirectory, "log");

    public static string UserName { get; } = Environment.UserName;

    public static string UserDirectoryRoot { get; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>
    /// Override the assembly-derived <see cref="ApplName"/> so that all path
    /// derivatives (<see cref="LocalApplDirectory"/>, <see cref="SettingsDirectory"/>,
    /// <see cref="TempDirectory"/>, <see cref="LogDirectory"/>) resolve under a
    /// shared name. Used by <c>oahu-cli</c> to coexist on the same machine as the
    /// Avalonia GUI by routing both front-ends to the GUI's <c>Oahu</c> data root.
    ///
    /// Must be called before any code path touches the affected directories or
    /// any type that captures them in static fields (e.g. before
    /// <c>Oahu.Core.AudibleClient</c>, <c>Oahu.BooksDatabase.BookDbContext</c>).
    /// </summary>
    public static void OverrideApplName(string name)
    {
      if (string.IsNullOrWhiteSpace(name))
      {
        throw new ArgumentException("name must not be null or empty", nameof(name));
      }

      ApplName = name;
    }

    private static T GetAttribute<T>() where T : Attribute
    {
      object[] attributes = EntryAssembly.GetCustomAttributes(typeof(T), false);
      if (attributes.Length == 0)
      {
        return null;
      }

      return attributes[0] as T;
    }

    private static Version GetOsVersion()
    {
      const string REGEX = @"\s([0-9.]+)";
      string os = RuntimeInformation.OSDescription;
      var regex = new Regex(REGEX);
      var match = regex.Match(os);
      if (!match.Success)
      {
        return new Version();
      }

      string osvers = match.Groups[1].Value;
      try
      {
        return new Version(osvers);
      }
      catch (Exception)
      {
        return new Version();
      }
    }
  }
}
