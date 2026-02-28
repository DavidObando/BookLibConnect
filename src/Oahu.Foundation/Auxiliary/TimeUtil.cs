using System;

namespace Oahu.Aux {
  public static class TimeUtil {
    private readonly static DateTime EPOCH = new(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

    private static double dateTimeToDouble (DateTime dt) {
      if (dt == default)
        return 0;

      TimeSpan ts = dt.Subtract (EPOCH);
      return ts.TotalSeconds;
    }

    public static int DateTimeToUnix32 (DateTime dt) => (int)dateTimeToDouble (dt);

    public static long DateTimeToUnix64 (DateTime dt) => (long)dateTimeToDouble (dt);

    public static long DateTimeToUnix64Msec (DateTime dt) {
      if (dt == default)
        return 0;

      TimeSpan ts = dt.Subtract (EPOCH);
      return (long)ts.TotalMilliseconds;
    }

    public static DateTime UnixToDateTime (long timestamp) {
      if (timestamp == 0)
        return default;

      DateTime dt = EPOCH.AddSeconds (timestamp);
      return dt;
    }

    public static DateTime UnixMsecToDateTime (long timestampMsec) {
      if (timestampMsec == 0)
        return default;

      DateTime dt = EPOCH.AddMilliseconds (timestampMsec);
      return dt;
    }

  }
}

