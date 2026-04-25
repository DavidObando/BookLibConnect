using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Oahu.BooksDatabase;
using Oahu.Cli.App.Core;
using Oahu.Cli.App.Models;
using Oahu.Common.Util;
using Oahu.Core;

namespace Oahu.Cli.App.Jobs;

/// <summary>
/// Real <see cref="IJobExecutor"/>: drives <see cref="DownloadDecryptJob{T}"/>
/// against the singleton <see cref="AudibleClient"/> exposed by
/// <see cref="CoreEnvironment"/>.
///
/// <para>
/// Bridges Core's push-based progress (<see cref="IProgress{T}"/> +
/// <c>OnNewStateCallback(Conversion)</c>) onto the pull-based
/// <see cref="IAsyncEnumerable{T}"/> the scheduler expects: progress events are
/// translated to <see cref="JobUpdate"/> records and forwarded through a
/// bounded channel; the run task is awaited in the background and the
/// terminal phase is appended once it finishes.
/// </para>
///
/// <para>
/// Phase 4c.1 only handles download + decrypt (<c>convertAction = null</c>).
/// AAX export ("Muxing") is wired in 4c.2 via the <c>convert</c> command.
/// </para>
/// </summary>
public sealed class AudibleJobExecutor : IJobExecutor
{
    private readonly Func<AudibleClient> clientFactory;
    private readonly Func<IDownloadSettings> downloadSettingsFactory;
    private readonly ILogger logger;

    public AudibleJobExecutor(ILogger<AudibleJobExecutor>? logger = null)
        : this(() => CoreEnvironment.Client, () => CoreEnvironment.Settings.DownloadSettings, logger)
    {
    }

    internal AudibleJobExecutor(
        Func<AudibleClient> clientFactory,
        Func<IDownloadSettings> downloadSettingsFactory,
        ILogger<AudibleJobExecutor>? logger = null)
    {
        this.clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        this.downloadSettingsFactory = downloadSettingsFactory ?? throw new ArgumentNullException(nameof(downloadSettingsFactory));
        this.logger = logger ?? NullLogger<AudibleJobExecutor>.Instance;
    }

    public async IAsyncEnumerable<JobUpdate> ExecuteAsync(
        JobRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Make sure the active GUI profile is loaded so the books DB query is
        // scoped to the right account; surfaces the same "no profile" error
        // shape as auth/library commands.
        if (!await CoreEnvironment.EnsureProfileLoadedAsync().ConfigureAwait(false))
        {
            yield return new JobUpdate
            {
                JobId = request.Id,
                Phase = JobPhase.Failed,
                Message = "No active profile. Run `oahu-cli auth login` first.",
            };
            yield break;
        }

        var client = clientFactory();
        var api = client.Api
            ?? throw new InvalidOperationException("AudibleClient.Api is null after EnsureProfileLoadedAsync returned true.");

        var book = api.GetBooks()?.FirstOrDefault(
            b => string.Equals(b.Asin, request.Asin, StringComparison.OrdinalIgnoreCase));
        if (book is null)
        {
            yield return new JobUpdate
            {
                JobId = request.Id,
                Phase = JobPhase.Failed,
                Message = $"ASIN '{request.Asin}' not found in the local library. Run `oahu-cli library sync` first.",
            };
            yield break;
        }

        var conversion = book.Conversion;
        if (conversion is null)
        {
            yield return new JobUpdate
            {
                JobId = request.Id,
                Phase = JobPhase.Failed,
                Message = $"Book '{request.Asin}' has no Conversion record (library cache is stale).",
            };
            yield break;
        }

        // Channel sized generously: progress events are cheap and the consumer
        // (the scheduler observer fan-out) drains continuously.
        var channel = Channel.CreateBounded<JobUpdate>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        var translator = new ProgressTranslator(request.Id, channel.Writer);
        translator.Emit(JobPhase.Licensing, message: "Requesting license");

        var progress = new Progress<ProgressMessage>(translator.OnProgress);
        Action<Conversion> onState = c => translator.OnStateChanged(c);

        var settings = downloadSettingsFactory();
        // Honour per-job quality without mutating the GUI-shared settings.
        var jobSettings = new PerJobDownloadSettings(settings, MapQuality(request.Quality));
        var context = new CliCancellation(cancellationToken);

        // Run the actual job in the background; the foreach below pulls the
        // translated updates from the channel.
        Task runTask = Task.Run(
            async () =>
            {
                try
                {
                    using var job = new DownloadDecryptJob<CliCancellation>(api, jobSettings, onState);
                    await job.DownloadDecryptAndConvertAsync(
                        new[] { conversion },
                        progress,
                        context,
                        convertAction: null).ConfigureAwait(false);
                }
                finally
                {
                    channel.Writer.TryComplete();
                }
            },
            cancellationToken);

        bool seenTerminal = false;
        while (await channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (channel.Reader.TryRead(out var update))
            {
                yield return update;
                if (update.Phase is JobPhase.Completed or JobPhase.Failed or JobPhase.Canceled)
                {
                    seenTerminal = true;
                }
            }
        }

        // The producer task may have thrown; surface that as Failed (unless we
        // were canceled, which the scheduler handles separately).
        Exception? runError = null;
        bool canceled = false;
        try
        {
            await runTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            canceled = true;
        }
        catch (Exception ex)
        {
            runError = ex;
        }

        if (canceled)
        {
            yield return new JobUpdate { JobId = request.Id, Phase = JobPhase.Canceled, Message = "Canceled" };
            yield break;
        }

        if (runError is not null)
        {
            logger.LogError(runError, "Download failed for {Asin} ({Title}).", request.Asin, request.Title);
            yield return new JobUpdate
            {
                JobId = request.Id,
                Phase = JobPhase.Failed,
                Message = runError.Message,
            };
            yield break;
        }

        if (!seenTerminal)
        {
            // Determine the outcome from the final Conversion state.
            var final = conversion.State;
            if (final is EConversionState.LocalUnlocked or EConversionState.Exported or EConversionState.Converted)
            {
                yield return new JobUpdate { JobId = request.Id, Phase = JobPhase.Completed };
            }
            else
            {
                yield return new JobUpdate
                {
                    JobId = request.Id,
                    Phase = JobPhase.Failed,
                    Message = $"Job ended in state '{final}'.",
                };
            }
        }
    }

    private static EDownloadQuality MapQuality(DownloadQuality q) => q switch
    {
        DownloadQuality.Normal => EDownloadQuality.Normal,
        DownloadQuality.High => EDownloadQuality.High,
        DownloadQuality.Extreme => EDownloadQuality.Extreme,
        _ => EDownloadQuality.High,
    };

    /// <summary>
    /// Translates Core's per-conversion <see cref="ProgressMessage"/> +
    /// <c>OnNewStateCallback(Conversion)</c> firehose into the coarse-grained
    /// <see cref="JobUpdate"/> stream the scheduler exposes. One translator
    /// per job (single ASIN), so we can safely accumulate in fields without
    /// extra synchronisation: progress callbacks are serialised on the
    /// <c>Progress&lt;T&gt;</c> sync context (or the thread pool when none).
    /// </summary>
    private sealed class ProgressTranslator
    {
        private readonly string jobId;
        private readonly ChannelWriter<JobUpdate> writer;
        private readonly object gate = new();
        private JobPhase current = JobPhase.Licensing;
        private int downloadPermille;
        private int decryptPercent;

        public ProgressTranslator(string jobId, ChannelWriter<JobUpdate> writer)
        {
            this.jobId = jobId;
            this.writer = writer;
        }

        public void Emit(JobPhase phase, double? progress = null, string? message = null)
        {
            lock (gate)
            {
                current = phase;
                writer.TryWrite(new JobUpdate
                {
                    JobId = jobId,
                    Phase = phase,
                    Progress = progress,
                    Message = message,
                });
            }
        }

        public void OnProgress(ProgressMessage msg)
        {
            lock (gate)
            {
                if (msg.IncStepsPerMille is int dl)
                {
                    downloadPermille = Math.Min(downloadPermille + dl, 1000);
                    if (current != JobPhase.Downloading)
                    {
                        current = JobPhase.Downloading;
                        writer.TryWrite(new JobUpdate { JobId = jobId, Phase = JobPhase.Downloading, Progress = downloadPermille / 1000.0 });
                    }
                    else
                    {
                        writer.TryWrite(new JobUpdate { JobId = jobId, Phase = JobPhase.Downloading, Progress = downloadPermille / 1000.0 });
                    }
                }
                if (msg.IncStepsPerCent is int dec)
                {
                    decryptPercent = Math.Min(decryptPercent + dec, 100);
                    if (current != JobPhase.Decrypting)
                    {
                        current = JobPhase.Decrypting;
                        writer.TryWrite(new JobUpdate { JobId = jobId, Phase = JobPhase.Decrypting, Progress = decryptPercent / 100.0 });
                    }
                    else
                    {
                        writer.TryWrite(new JobUpdate { JobId = jobId, Phase = JobPhase.Decrypting, Progress = decryptPercent / 100.0 });
                    }
                }
            }
        }

        public void OnStateChanged(Conversion conversion)
        {
            if (conversion is null)
            {
                return;
            }

            lock (gate)
            {
                switch (conversion.State)
                {
                    case EConversionState.LicenseGranted:
                        if (current == JobPhase.Licensing)
                        {
                            // Licensing succeeded but not yet downloading; keep the phase but emit a heartbeat.
                            writer.TryWrite(new JobUpdate { JobId = jobId, Phase = JobPhase.Licensing, Message = "License granted" });
                        }
                        break;
                    case EConversionState.Downloading:
                        if (current != JobPhase.Downloading)
                        {
                            current = JobPhase.Downloading;
                            writer.TryWrite(new JobUpdate { JobId = jobId, Phase = JobPhase.Downloading, Progress = 0 });
                        }
                        break;
                    case EConversionState.LocalLocked:
                        if (current is JobPhase.Licensing or JobPhase.Downloading)
                        {
                            current = JobPhase.Downloading;
                            writer.TryWrite(new JobUpdate { JobId = jobId, Phase = JobPhase.Downloading, Progress = 1 });
                        }
                        break;
                    case EConversionState.Unlocking:
                        if (current != JobPhase.Decrypting)
                        {
                            current = JobPhase.Decrypting;
                            writer.TryWrite(new JobUpdate { JobId = jobId, Phase = JobPhase.Decrypting, Progress = 0 });
                        }
                        break;
                    case EConversionState.LocalUnlocked:
                    case EConversionState.Exported:
                    case EConversionState.Converted:
                        if (current is not JobPhase.Completed)
                        {
                            current = JobPhase.Completed;
                            writer.TryWrite(new JobUpdate { JobId = jobId, Phase = JobPhase.Completed });
                        }
                        break;
                    case EConversionState.LicenseDenied:
                        if (current is not JobPhase.Failed)
                        {
                            current = JobPhase.Failed;
                            writer.TryWrite(new JobUpdate { JobId = jobId, Phase = JobPhase.Failed, Message = "License denied" });
                        }
                        break;
                    case EConversionState.DownloadError:
                        if (current is not JobPhase.Failed)
                        {
                            current = JobPhase.Failed;
                            writer.TryWrite(new JobUpdate { JobId = jobId, Phase = JobPhase.Failed, Message = "Download failed" });
                        }
                        break;
                    case EConversionState.UnlockingFailed:
                        if (current is not JobPhase.Failed)
                        {
                            current = JobPhase.Failed;
                            writer.TryWrite(new JobUpdate { JobId = jobId, Phase = JobPhase.Failed, Message = "Decryption failed" });
                        }
                        break;
                    default:
                        break;
                }
            }
        }
    }
}
