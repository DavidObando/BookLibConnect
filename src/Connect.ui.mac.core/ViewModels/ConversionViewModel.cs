using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using core.audiamus.booksdb;

namespace core.audiamus.connect.ui.mac.ViewModels {
  public partial class ConversionViewModel : ObservableObject {

    [ObservableProperty]
    private ObservableCollection<ConversionItemViewModel> _conversions = new ();

    [ObservableProperty]
    private bool _isIdle = true;

    [ObservableProperty]
    private bool _downloadOnlyMode;

    public void AddConversion (Book book) {
      Conversions.Add (new ConversionItemViewModel (book));
    }

    public void Clear () {
      Conversions.Clear ();
    }

    [RelayCommand]
    private void Cancel () {
      // Cancellation is handled via the CancellationToken in the business logic
    }
  }

  public partial class ConversionItemViewModel : ObservableObject {
    private readonly Book _book;

    public ConversionItemViewModel (Book book) {
      _book = book;
    }

    public string Title => _book.Title;
    public string Author => _book.Author;
    public string Asin => _book.Asin;

    [ObservableProperty]
    private EConversionState _state;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _statusText;

    public void UpdateState (EConversionState state) {
      State = state;
      StatusText = state.ToString ();
    }

    public void UpdateProgress (double value) {
      Progress = Math.Clamp (value, 0.0, 1.0);
    }
  }
}
