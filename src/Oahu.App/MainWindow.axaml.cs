using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using Oahu.Aux;
using Oahu.Aux.Extensions;
using Oahu.BooksDatabase;
using Oahu.Common.Util;
using Oahu.Core;
using Oahu.Core.UI.Avalonia.ViewModels;
using static Oahu.Aux.Logging;

namespace Oahu.App.Avalonia
{
  public partial class MainWindow : Window
  {
    private readonly MainWindowViewModel viewModel;
    private readonly UserSettings userSettings;
    private bool initDone;
    private CancellationTokenSource cts;
    private WindowNotificationManager notificationManager;

    public MainWindow()
    {
      InitializeComponent();
    }

    public MainWindow(MainWindowViewModel viewModel, UserSettings userSettings) : this()
    {
      this.viewModel = viewModel;
      this.userSettings = userSettings;
      DataContext = viewModel;
    }

    protected override async void OnOpened(EventArgs e)
    {
      base.OnOpened(e);
      if (initDone || viewModel is null)
      {
        return;
      }

      initDone = true;
      await InitAsync();
    }

    private async Task InitAsync()
    {
      using var logGuard = new LogGuard(3, this);

      notificationManager = new WindowNotificationManager(this)
      {
        Position = NotificationPosition.BottomRight,
        MaxItems = 3
      };

      viewModel.SetBusy(true, "Initializing...");

      try
      {
        var client = viewModel.AudibleClient;

        // Run setup wizard if no profiles exist (mirrors Windows runWizardAsync)
        Log(4, this, () => "before wizard");
        await RunWizardAsync(client);

        // Initialize the database (mirrors Windows init)
        Log(4, this, () => "before db");
        viewModel.SetBusy(true, "Initializing database...");
        await BookDbContextLazyLoad.StartupAsync();

        // Load profile from config file (mirrors Windows ConfigFromFileAsync)
        Log(4, this, () => "before config");
        viewModel.SetBusy(true, "Loading configuration...");
        viewModel.CurrentProfile = await client.ConfigFromFileAsync(
          userSettings.DownloadSettings?.Profile,
          GetAccountAlias);

        if (viewModel.CurrentProfile is not null)
        {
          userSettings.DownloadSettings.Profile = new ProfileAliasKey(viewModel.CurrentProfile);
          userSettings.Save();

          // Initialize the API and library (mirrors Windows initLibraryAsync)
          viewModel.Api = client.Api;
          if (viewModel.Api is not null)
          {
            viewModel.Api.GetAccountAliasFunc = GetAccountAlias;

            Log(3, this, () => $"Profile loaded: region={client.ProfileKey?.Region}, " +
              $"account={client.ProfileKey?.AccountId ?? "(null)"}");

            if (userSettings.DownloadSettings.AutoUpdateLibrary)
            {
              viewModel.SetBusy(true, "Updating library...");
              var libraryResult = await viewModel.Api.GetLibraryAsync(false);
              if (libraryResult is null)
              {
                Log(1, this, () => "Library sync returned null — API call likely failed. Check log for HTTP error details.");
                viewModel.StatusMessage = "Warning: Library sync failed. See logs for details.";
              }
              else
              {
                Log(3, this, () => $"Library sync: {libraryResult.Items?.Length ?? 0} book(s)");
              }

              viewModel.SetBusy(true, "Downloading cover images...");
              await viewModel.Api.DownloadCoverImagesAsync();
            }
            else
            {
              Log(3, this, () => "AutoUpdateLibrary is disabled, skipping library sync");
            }

            // Verify that completed downloads still have their output files on disk.
            // If a file is missing, the conversion state is reset to Remote.
            viewModel.SetBusy(true, "Verifying downloaded files...");
            int resetCount = viewModel.Api.VerifyCompletedDownloads(
              userSettings.DownloadSettings,
              userSettings.ExportSettings);
            if (resetCount > 0)
            {
              Log(3, this, () => $"{resetCount} book(s) reset to Remote (output files missing)");
            }

            // Load books into the library view
            var books = viewModel.Api.GetBooks();
            Log(3, this, () => $"Local books: {books?.Count() ?? 0}");
            if (books is not null)
            {
              viewModel.BookLibrary.LoadBooks(books);
            }

            // Wire download button to move selected books to Downloads tab
            viewModel.BookLibrary.DownloadRequested += OnDownloadRequested;

            // Wire the download pipeline
            viewModel.Conversion.RunRequested += OnRunDownloadPipeline;
            viewModel.Conversion.CancelRequested += OnCancelDownload;
          }
          else
          {
            Log(1, this, () => "API is null after profile load — profile may be incomplete");
            viewModel.StatusMessage = "Warning: API not initialized. Profile may be incomplete.";
          }
        }
        else
        {
          Log(1, this, () => "CurrentProfile is null after ConfigFromFileAsync");
          viewModel.StatusMessage = "Warning: No profile loaded. Try signing in again.";
        }

        viewModel.SetBusy(false, "Ready");
        viewModel.IsInitialized = true;
        Log(4, this, () => "all done");
      }
      catch (Exception ex)
      {
        Log(1, this, () => $"init error: {ex.Message}");
        viewModel.SetBusy(false, $"Initialization error: {ex.Message}");
      }
    }

    private async Task RunWizardAsync(AudibleClient client)
    {
      using var logGuard = new LogGuard(3, this);

      var profiles = await client.GetProfilesAsync();
      bool needsProfile = profiles.IsNullOrEmpty();

      if (!needsProfile)
      {
        Log(3, this, () => "profiles exist, skipping wizard");
        return;
      }

      Log(3, this, () => "no profiles found, showing setup wizard");

      var wizardVm = new ProfileWizardViewModel();
      wizardVm.SetClient(client);
      wizardVm.SetSettings(userSettings.DownloadSettings, userSettings.ExportSettings);

      var wizardWindow = new SetupWizardWindow(wizardVm);
      await wizardWindow.ShowWizardAsync(this);

      if (!wizardVm.RegistrationSucceeded)
      {
        Log(1, this, () => "wizard: no profile was created");
        viewModel.StatusMessage = "Warning: No profile was created. You can create one later via Settings.";
      }
    }

    private void OnDownloadRequested(object sender, IEnumerable<BookItemViewModel> selectedBooks)
    {
      var books = selectedBooks.ToList();
      Log(3, this, () => $"download requested for {books.Count} book(s)");

      foreach (var bookVm in books)
      {
        viewModel.Conversion.AddConversion(bookVm.Book);
      }

      viewModel.StatusMessage = $"{viewModel.Conversion.QueuedCount} book(s) queued for download.";
    }

    private void OnCancelDownload()
    {
      Log(3, this, () => "cancel requested");
      cts?.Cancel();
    }

    private async Task OnRunDownloadPipeline(IReadOnlyList<ConversionItemViewModel> items)
    {
      using var lg = new LogGuard(3, this, () => $"#items={items.Count}");

      cts = new CancellationTokenSource();
      var api = viewModel.Api;
      if (api is null)
      {
        viewModel.StatusMessage = "Error: API not initialized.";
        return;
      }

      var conversions = items
        .Select(i => i.Conversion)
        .Where(c => c is not null)
        .ToList();

      if (conversions.Count == 0)
      {
        viewModel.StatusMessage = "No downloadable items in queue.";
        return;
      }

      // Lookup from Conversion to UI item for progress updates
      var lookup = items.ToDictionary(i => i.Asin);

      int totalItems = conversions.Count;
      int completedItems = 0;

      // Per-item accumulated progress for download (permille) and decrypt (percent)
      var downloadPermille = new Dictionary<string, int>();
      var decryptPercent = new Dictionary<string, int>();
      var itemProgress = new Dictionary<string, double>();

      var progress = new Progress<ProgressMessage>(msg =>
      {
        Dispatcher.UIThread.Post(() =>
        {
          if (msg.IncItem.HasValue)
          {
            completedItems += msg.IncItem.Value;
          }

          // Per-item download progress (0% to 50% of item bar)
          if (msg.IncStepsPerMille.HasValue && msg.Asin is not null
            && lookup.TryGetValue(msg.Asin, out var dlItem))
          {
            int accumulated = downloadPermille.GetValueOrDefault(msg.Asin) + msg.IncStepsPerMille.Value;
            downloadPermille[msg.Asin] = accumulated;
            double p = Math.Min(accumulated / 1000.0, 1.0) * 0.5;
            itemProgress[msg.Asin] = p;
            dlItem.UpdateProgress(p);
          }

          // Per-item decrypt progress (50% to 100% of item bar)
          if (msg.IncStepsPerCent.HasValue && msg.Asin is not null
            && lookup.TryGetValue(msg.Asin, out var decItem))
          {
            int accumulated = decryptPercent.GetValueOrDefault(msg.Asin) + msg.IncStepsPerCent.Value;
            decryptPercent[msg.Asin] = accumulated;
            double p = 0.5 + Math.Min(accumulated / 100.0, 1.0) * 0.5;
            itemProgress[msg.Asin] = p;
            decItem.UpdateProgress(p);
          }

          // Overall progress: average of all per-item progress
          double overallPct = totalItems > 0
            ? itemProgress.Values.Sum() / totalItems
            : 0;
          viewModel.Conversion.UpdateOverallProgress(overallPct,
            $"Processing {completedItems} of {totalItems}...");
        });
      });

      Action<Conversion> onStateChanged = conv =>
      {
        Dispatcher.UIThread.Post(() =>
        {
          if (conv?.Book?.Asin is null)
          {
            return;
          }

          if (!lookup.TryGetValue(conv.Book.Asin, out var itemVm))
          {
            return;
          }

          itemVm.UpdateState(conv.State);

          // Check if this item has reached a terminal success state
          bool done = conv.State is EConversionState.LocalUnlocked
            or EConversionState.Exported
            or EConversionState.Converted;

          if (done)
          {
            // Mark item as fully complete for overall progress calculation
            itemProgress[conv.Book.Asin] = 1.0;
            itemVm.UpdateProgress(1.0);

            string title = conv.Book.Title ?? conv.Book.Asin;
            string stateLabel = conv.State switch
            {
              EConversionState.Exported => "downloaded and exported",
              _ => "downloaded and decrypted"
            };
            notificationManager?.Show(
              new Notification(
                "Download Complete",
                $"\"{title}\" has been {stateLabel}.",
                NotificationType.Success));
            viewModel.Conversion.RemoveConversion(conv.Book.Asin);
            lookup.Remove(conv.Book.Asin);
          }
        });
      };

      // Build the export action
      bool doExport = userSettings.ExportSettings?.ExportToAax ?? false;
      AaxExporter exporter = null;
      if (doExport)
      {
        exporter = new AaxExporter(userSettings.ExportSettings, userSettings.DownloadSettings);
      }

      viewModel.StatusMessage = "Downloading...";

      try
      {
        using var job = new DownloadDecryptJob<SimpleCancellation>(
          api,
          userSettings.DownloadSettings,
          onStateChanged);

        ConvertDelegate<SimpleCancellation> convertAction = null;
        if (doExport && exporter is not null)
        {
          convertAction = (book, ctx, callback) =>
          {
            exporter.Export(book, new SimpleConversionContext(null, ctx.CancellationToken), callback);
          };
        }

        var context = new SimpleCancellation(cts.Token);

        await job.DownloadDecryptAndConvertAsync(
          conversions,
          progress,
          context,
          convertAction);

        viewModel.StatusMessage = cts.IsCancellationRequested
          ? "Download cancelled."
          : "Download complete.";
      }
      catch (OperationCanceledException)
      {
        viewModel.StatusMessage = "Download cancelled.";
      }
      catch (Exception ex)
      {
        Log(1, this, () => $"pipeline error: {ex.Message}");
        viewModel.StatusMessage = $"Download error: {ex.Message}";
      }
      finally
      {
        cts?.Dispose();
        cts = null;

        // Refresh states for any items still in the queue (errors, cancelled, etc.)
        foreach (var kvp in lookup)
        {
          var item = items.FirstOrDefault(i => i.Asin == kvp.Key);
          if (item?.Conversion is not null)
          {
            item.UpdateState(item.Conversion.State);
          }
        }

        viewModel.Conversion.UpdateOverallProgress(1.0, "Finished");
        viewModel.Conversion.UpdateQueuedCount();
      }
    }

    private bool GetAccountAlias(AccountAliasContext ctxt)
    {
      // Auto-accept the alias with customer name for now
      if (ctxt.Alias.IsNullOrWhiteSpace())
      {
        ctxt.Alias = ctxt.CustomerName;
      }

      return true;
    }
  }
}
