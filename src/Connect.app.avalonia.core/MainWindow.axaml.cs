using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Oahu.Aux;
using Oahu.Aux.Extensions;
using Oahu.BooksDatabase;
using Oahu.Core;
using Oahu.Core.UI.Avalonia.ViewModels;
using Oahu.Common.Util;
using static Oahu.Aux.Logging;

namespace Oahu.App.Avalonia {
  public partial class MainWindow : Window {

    private readonly MainWindowViewModel _viewModel;
    private readonly UserSettings _userSettings;
    private bool _initDone;
    private CancellationTokenSource _cts;
    private WindowNotificationManager _notificationManager;

    public MainWindow () {
      InitializeComponent ();
    }

    public MainWindow (MainWindowViewModel viewModel, UserSettings userSettings) : this () {
      _viewModel = viewModel;
      _userSettings = userSettings;
      DataContext = viewModel;
    }

    protected override async void OnOpened (EventArgs e) {
      base.OnOpened (e);
      if (_initDone || _viewModel is null)
        return;

      _initDone = true;
      await initAsync ();
    }

    private async Task initAsync () {
      using var _ = new LogGuard (3, this);

      _notificationManager = new WindowNotificationManager (this) {
        Position = NotificationPosition.BottomRight,
        MaxItems = 3
      };

      _viewModel.SetBusy (true, "Initializing...");

      try {
        var client = _viewModel.AudibleClient;

        // Run setup wizard if no profiles exist (mirrors Windows runWizardAsync)
        Log (4, this, () => "before wizard");
        await runWizardAsync (client);

        // Initialize the database (mirrors Windows init)
        Log (4, this, () => "before db");
        _viewModel.SetBusy (true, "Initializing database...");
        await BookDbContextLazyLoad.StartupAsync ();

        // Load profile from config file (mirrors Windows ConfigFromFileAsync)
        Log (4, this, () => "before config");
        _viewModel.SetBusy (true, "Loading configuration...");
        _viewModel.CurrentProfile = await client.ConfigFromFileAsync (
          _userSettings.DownloadSettings?.Profile,
          getAccountAlias
        );

        if (_viewModel.CurrentProfile is not null) {
          _userSettings.DownloadSettings.Profile = new ProfileAliasKey (_viewModel.CurrentProfile);
          _userSettings.Save ();

          // Initialize the API and library (mirrors Windows initLibraryAsync)
          _viewModel.Api = client.Api;
          if (_viewModel.Api is not null) {
            _viewModel.Api.GetAccountAliasFunc = getAccountAlias;

            if (_userSettings.DownloadSettings.AutoUpdateLibrary) {
              _viewModel.SetBusy (true, "Updating library...");
              await _viewModel.Api.GetLibraryAsync (false);

              _viewModel.SetBusy (true, "Downloading cover images...");
              await _viewModel.Api.DownloadCoverImagesAsync ();
            }

            // Load books into the library view
            var books = _viewModel.Api.GetBooks ();
            if (books is not null)
              _viewModel.BookLibrary.LoadBooks (books);

            // Wire download button to move selected books to Downloads tab
            _viewModel.BookLibrary.DownloadRequested += onDownloadRequested;

            // Wire the download pipeline
            _viewModel.Conversion.RunRequested += onRunDownloadPipeline;
            _viewModel.Conversion.CancelRequested += onCancelDownload;
          }
        }

        _viewModel.SetBusy (false, "Ready");
        _viewModel.IsInitialized = true;
        Log (4, this, () => "all done");
      } catch (Exception ex) {
        Log (1, this, () => $"init error: {ex.Message}");
        _viewModel.SetBusy (false, $"Initialization error: {ex.Message}");
      }
    }

    private async Task runWizardAsync (AudibleClient client) {
      using var _ = new LogGuard (3, this);

      var profiles = await client.GetProfilesAsync ();
      bool needsProfile = profiles.IsNullOrEmpty ();

      if (!needsProfile) {
        Log (3, this, () => "profiles exist, skipping wizard");
        return;
      }

      Log (3, this, () => "no profiles found, showing setup wizard");

      var wizardVm = new ProfileWizardViewModel ();
      wizardVm.SetClient (client);
      wizardVm.SetSettings (_userSettings.DownloadSettings, _userSettings.ExportSettings);

      var wizardWindow = new SetupWizardWindow (wizardVm);
      await wizardWindow.ShowWizardAsync (this);

      if (!wizardVm.RegistrationSucceeded) {
        Log (1, this, () => "wizard: no profile was created");
        _viewModel.StatusMessage = "Warning: No profile was created. You can create one later via Settings.";
      }
    }

    private void onDownloadRequested (object sender, IEnumerable<BookItemViewModel> selectedBooks) {
      var books = selectedBooks.ToList ();
      Log (3, this, () => $"download requested for {books.Count} book(s)");

      foreach (var bookVm in books)
        _viewModel.Conversion.AddConversion (bookVm.Book);

      _viewModel.StatusMessage = $"{_viewModel.Conversion.QueuedCount} book(s) queued for download.";
    }

    private void onCancelDownload () {
      Log (3, this, () => "cancel requested");
      _cts?.Cancel ();
    }

    private async Task onRunDownloadPipeline (IReadOnlyList<ConversionItemViewModel> items) {
      using var lg = new LogGuard (3, this, () => $"#items={items.Count}");

      _cts = new CancellationTokenSource ();
      var api = _viewModel.Api;
      if (api is null) {
        _viewModel.StatusMessage = "Error: API not initialized.";
        return;
      }

      var conversions = items
        .Select (i => i.Conversion)
        .Where (c => c is not null)
        .ToList ();

      if (conversions.Count == 0) {
        _viewModel.StatusMessage = "No downloadable items in queue.";
        return;
      }

      // Lookup from Conversion to UI item for progress updates
      var lookup = items.ToDictionary (i => i.Asin);

      int totalItems = conversions.Count;
      int completedItems = 0;

      var progress = new Progress<ProgressMessage> (msg => {
        Dispatcher.UIThread.Post (() => {
          if (msg.IncItem.HasValue)
            completedItems += msg.IncItem.Value;

          double pct = totalItems > 0 ? (double)completedItems / totalItems : 0;
          _viewModel.Conversion.UpdateOverallProgress (pct,
            $"Processing {completedItems} of {totalItems}...");
        });
      });

      Action<Conversion> onStateChanged = conv => {
        Dispatcher.UIThread.Post (() => {
          if (conv?.Book?.Asin is null)
            return;
          if (!lookup.TryGetValue (conv.Book.Asin, out var itemVm))
            return;

          itemVm.UpdateState (conv.State);

          // Check if this item has reached a terminal success state
          bool done = conv.State is EConversionState.local_unlocked
            or EConversionState.exported
            or EConversionState.converted;

          if (done) {
            string title = conv.Book.Title ?? conv.Book.Asin;
            string stateLabel = conv.State switch {
              EConversionState.exported => "downloaded and exported",
              _ => "downloaded and decrypted"
            };
            _notificationManager?.Show (
              new Notification (
                "Download Complete",
                $"\"{title}\" has been {stateLabel}.",
                NotificationType.Success
              ));
            _viewModel.Conversion.RemoveConversion (conv.Book.Asin);
            lookup.Remove (conv.Book.Asin);
          }
        });
      };

      // Build the export action
      bool doExport = _userSettings.ExportSettings?.ExportToAax ?? false;
      AaxExporter exporter = null;
      if (doExport) {
        exporter = new AaxExporter (_userSettings.ExportSettings, _userSettings.DownloadSettings);
      }

      _viewModel.StatusMessage = "Downloading...";

      try {
        using var job = new DownloadDecryptJob<SimpleCancellation> (
          api,
          _userSettings.DownloadSettings,
          onStateChanged
        );

        ConvertDelegate<SimpleCancellation> convertAction = null;
        if (doExport && exporter is not null) {
          convertAction = (book, ctx, callback) => {
            exporter.Export (book, new SimpleConversionContext (null, ctx.CancellationToken), callback);
          };
        }

        var context = new SimpleCancellation (_cts.Token);

        await job.DownloadDecryptAndConvertAsync (
          conversions,
          progress,
          context,
          convertAction
        );

        _viewModel.StatusMessage = _cts.IsCancellationRequested
          ? "Download cancelled."
          : "Download complete.";

      } catch (OperationCanceledException) {
        _viewModel.StatusMessage = "Download cancelled.";
      } catch (Exception ex) {
        Log (1, this, () => $"pipeline error: {ex.Message}");
        _viewModel.StatusMessage = $"Download error: {ex.Message}";
      } finally {
        _cts?.Dispose ();
        _cts = null;

        // Refresh states for any items still in the queue (errors, cancelled, etc.)
        foreach (var kvp in lookup) {
          var item = items.FirstOrDefault (i => i.Asin == kvp.Key);
          if (item?.Conversion is not null)
            item.UpdateState (item.Conversion.State);
        }

        _viewModel.Conversion.UpdateOverallProgress (1.0, "Finished");
        _viewModel.Conversion.UpdateQueuedCount ();
      }
    }

    private bool getAccountAlias (AccountAliasContext ctxt) {
      // Auto-accept the alias with customer name for now
      if (ctxt.Alias.IsNullOrWhiteSpace ())
        ctxt.Alias = ctxt.CustomerName;
      return true;
    }
  }
}
