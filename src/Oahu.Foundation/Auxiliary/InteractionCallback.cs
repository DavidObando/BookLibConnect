using System;
using System.Diagnostics.Contracts;
using System.Threading;

namespace Oahu.Aux
{
  /// <summary>
  /// Provides an IInteractCallback{T, TResult} that invokes callbacks for interaction on the captured SynchronizationContext.
  /// </summary>
  public class InteractionCallback<T, TResult> : IInteractionCallback<T, TResult>
  {
    private static readonly SynchronizationContext DefaultContext = new SynchronizationContext();

    private readonly SynchronizationContext _synchronizationContext;
    private readonly Func<T, TResult> _handler;

    public InteractionCallback(Func<T, TResult> handler)
    {
      _synchronizationContext = SynchronizationContext.Current ?? DefaultContext;
      Contract.Assert(_synchronizationContext != null);
      if (handler is null)
      {
        throw new ArgumentNullException(nameof(handler));
      }

      _handler = handler;
    }

    TResult IInteractionCallback<T, TResult>.Interact(T value) => onInteract(value);

    protected virtual TResult onInteract(T value)
    {
      // If there's no handler, don't bother going through the sync context.
      TResult retval = default(TResult);
      if (_handler != null)
      {
        // Post the processing to the sync context.
        // (If T is a value type, it will get boxed here.)
        _synchronizationContext.Send(new SendOrPostCallback((x) =>
        {
          retval = _handler(value);
        }),
        null);
      }

      return retval;
    }
  }
}
