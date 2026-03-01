using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Oahu.Decrypt.FrameFilters
{
  public abstract class FrameFilterBase<TInput> : IDisposable
  {
    private readonly Channel<BufferEntry> filterChannel;
    private CancellationToken CancellationToken;
    private Task? filterLoop;
    private TInput[] buffer;
    private int bufferPosition = 0;

    public FrameFilterBase()
    {
      filterChannel = Channel.CreateBounded<BufferEntry>(new BoundedChannelOptions(2) { SingleReader = true, SingleWriter = true });
      buffer = new TInput[InputBufferSize];
    }

    ~FrameFilterBase()
    {
      Dispose(disposing: false);
    }

    protected bool Disposed { get; private set; }

    protected abstract int InputBufferSize { get; }

    public virtual async Task AddInputAsync(TInput input)
    {
      filterLoop ??= Task.Run(Encoder, CancellationToken);

      if (CancellationToken.IsCancellationRequested)
      {
        return;
      }

      buffer[bufferPosition++] = input;

      if (bufferPosition == InputBufferSize)
      {
        if (await filterChannel.Writer.WaitToWriteAsync(CancellationToken))
        {
          await filterChannel.Writer.WriteAsync(new BufferEntry(bufferPosition, buffer), CancellationToken);
          bufferPosition = 0;
          buffer = new TInput[InputBufferSize];
        }
      }
    }

    public Task CompleteAsync() => CompleteInternalAsync();

    public void Dispose()
    {
      Dispose(disposing: true);
      GC.SuppressFinalize(this);
    }

    public virtual void SetCancellationToken(CancellationToken cancellationToken) => CancellationToken = cancellationToken;

    protected abstract Task FlushAsync();

    protected abstract Task HandleInputDataAsync(TInput input);

    protected virtual async Task CompleteInternalAsync()
    {
      try
      {
        await filterChannel.Writer.WriteAsync(new BufferEntry(bufferPosition, buffer), CancellationToken);
        filterChannel.Writer.Complete();
      }
      catch (OperationCanceledException)
      {
      }

      if (filterLoop is not null)
      {
        await filterLoop;
      }
    }

    protected virtual void Dispose(bool disposing)
    {
      if (disposing && !Disposed)
      {
        if (filterLoop?.IsCompleted is false)
        {
          filterChannel.Writer.TryComplete();
        }
      }

      Disposed = true;
    }

    private async Task Encoder()
    {
      try
      {
        while (await filterChannel.Reader.WaitToReadAsync(CancellationToken))
        {
          await foreach (var messages in filterChannel.Reader.ReadAllAsync(CancellationToken))
          {
            for (int i = 0; i < messages.NumEntries; i++)
            {
              await HandleInputDataAsync(messages.Entries[i]);
            }
          }
        }

        await FlushAsync();
      }
      catch (Exception ex)
      {
        filterChannel.Writer.Complete(ex);
        throw;
      }
    }

    private record BufferEntry(int NumEntries, TInput[] Entries);
  }
}
