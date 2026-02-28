using System.Resources;

namespace Oahu.Aux.Extensions
{
  public static class ResourceManagerEx
  {
    public static string GetStringEx(this ResourceManager rm, string val)
    {
      if (rm is null)
      {
        return val;
      }

      string s = null;
      try
      {
        s = rm.GetString(val.ToLowerInvariant());
      }
      catch (MissingManifestResourceException)
      {
      }

      return s ?? val;
    }
  }
}
