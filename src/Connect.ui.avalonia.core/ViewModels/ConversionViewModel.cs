using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BookLibConnect.BooksDatabase;

namespace BookLibConnect.Core.UI.Avalonia.ViewModels {
  public partial class ConversionViewModel : ObservableObject {

    [ObservableProperty]
    private ObservableCollection<ConversionItemViewModel> _conversions = new ();

    [ObservableProperty]
    private bool _isIdle = true;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private int _queuedCount;

    [ObservableProperty]
    private double _overallProgress;

    [ObservableProperty]
    private string _overallStatusText;

    /// <summary>
    /// Raised when the user clicks Run. The MainWindow handles the actual pipeline.
    /// </summary>
    public event Func<IReadOnlyList<ConversionItemViewModel>, Task> RunRequested;

    /// <summary>
    /// Raised when the user clicks Cancel during a running pipeline.
    /// </summary>
    public event Action CancelRequested;

    public void AddConversion (Book book) {
      // Avoid duplicates
      if (Conversions.Any (c => c.Asin == book.Asin))
        return;
      Conversions.Add (new ConversionItemViewModel (book));
      UpdateQueuedCount ();
    }

    public void Clear () {
      Conversions.Clear ();
      UpdateQueuedCount ();
    }

    public bool RemoveConversion (string asin) {
      var item = Conversions.FirstOrDefault (c => c.Asin == asin);
      if (item is null)
        return false;
      Conversions.Remove (item);
      UpdateQueuedCount ();
      return true;
    }

    public void UpdateQueuedCount () =>
      QueuedCount = Conversions.Count;

    [RelayCommand]
    private void RemoveSelected () {
      var toRemove = Conversions.Where (c => c.IsSelected).ToList ();
      foreach (var item in toRemove)
        Conversions.Remove (item);
      UpdateQueuedCount ();
    }

    [RelayCommand]
    private async Task Run () {
      if (Conversions.Count == 0 || RunRequested is null)
        return;

      IsRunning = true;
      IsIdle = false;
      OverallProgress = 0;
      OverallStatusText = "Starting...";

      try {
        await RunRequested.Invoke (Conversions.ToList ().AsReadOnly ());
      } finally {
        IsRunning = false;
        IsIdle = true;
        OverallStatusText = "Finished";
      }
    }

    [RelayCommand]
    private void Cancel () {
      CancelRequested?.Invoke ();
    }

    public void UpdateOverallProgress (double progress, string status) {
      OverallProgress = progress;
      if (status is not null)
        OverallStatusText = status;
    }
  }

  public partial class ConversionItemViewModel : ObservableObject {
    private readonly Book _book;

    public ConversionItemViewModel (Book book) {
      _book = book;
    }

    public Book Book => _book;
    public string Title => _book.Title;
    public string Author => _book.Author;
    public string Asin => _book.Asin;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private EConversionState _state;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _statusText = "Queued";

    public Conversion Conversion => _book.Conversion;

    public void UpdateState (EConversionState state) {
      State = state;
      StatusText = state switch {
        EConversionState.unknown => "Queued",
        EConversionState.license_granted => "Licensed",
        EConversionState.license_denied => "License denied",
        EConversionState.local_locked => "Downloaded",
        EConversionState.download_error => "Download error",
        EConversionState.local_unlocked => "Decrypted",
        EConversionState.unlocking_failed => "Decrypt error",
        EConversionState.exported => "Exported",
        EConversionState.conversion_error => "Export error",
        _ => state.ToString ()
      };
    }

    public void UpdateProgress (double value) {
      Progress = Math.Clamp (value, 0.0, 1.0);
    }
  }
}
