using System;

namespace Oahu.Common.Util
{
  public abstract class ThreadProgressBase<T> : IDisposable
  {
    private readonly Action<T> _report;

    private int _accuValuePerMax;

    protected ThreadProgressBase(Action<T> report)
    {
      _report = report;
    }

    protected abstract int Max { get; }

    public void Dispose()
    {
      int inc = Max - _accuValuePerMax;
      if (inc > 0)
      {
        _report?.Invoke(getProgressMessage(inc));
      }
    }

    public void Report(double value)
    {
      int val = (int)(value * Max);
      int total = Math.Min(Max, val);
      int inc = total - _accuValuePerMax;
      _accuValuePerMax = total;
      if (inc > 0)
      {
        _report?.Invoke(getProgressMessage(inc));
      }
    }

    protected abstract T getProgressMessage(int inc);
  }

  public class ThreadProgressPerMille : ThreadProgressBase<ProgressMessage>
  {
    public ThreadProgressPerMille(Action<ProgressMessage> report) : base(report)
    {
    }

    protected override int Max => 1000;

    protected override ProgressMessage getProgressMessage(int inc) => new(null, null, null, inc);
  }

  public class ThreadProgressPerCent : ThreadProgressBase<ProgressMessage>
  {
    public ThreadProgressPerCent(Action<ProgressMessage> report) : base(report)
    {
    }

    protected override int Max => 100;

    protected override ProgressMessage getProgressMessage(int inc) => new(null, null, inc, null);
  }
}
