using System;
using System.Text.Json;
using Oahu.Aux;
using Oahu.Aux.Extensions;

namespace Oahu.Audible.Json
{
  public abstract class Serialization<T>
  {
    private static JsonSerializerOptions Options { get; } = JsonExtensions.Options;

    public string Serialize()
    {
      return JsonSerializer.Serialize(this, typeof(T), Options);
    }

    public static T Deserialize(string json)
    {
      try
      {
        return JsonSerializer.Deserialize<T>(json, Options);
      }
      catch (Exception exc)
      {
        Logging.Log(1, typeof(Serialization<T>), () => exc.Summary());
        return default;
      }
    }
  }
}
