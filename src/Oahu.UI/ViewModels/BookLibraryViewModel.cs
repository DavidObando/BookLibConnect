using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oahu.Aux;
using Oahu.BooksDatabase;
using Oahu.Core;

namespace Oahu.Core.UI.Avalonia.ViewModels
{
  public partial class BookLibraryViewModel : ObservableObject
  {
    private IDownloadSettings downloadSettings;

    [ObservableProperty]
    private ObservableCollection<BookItemViewModel> books = new();

    [ObservableProperty]
    private BookItemViewModel selectedBook;

    [ObservableProperty]
    private bool hasSelectedBook;

    [ObservableProperty]
    private string filterText;

    [ObservableProperty]
    private bool downloadSelectEnabled;

    [ObservableProperty]
    private int selectedCount;

    public BookLibraryViewModel(IDownloadSettings downloadSettings = null)
    {
      SetDownloadSettings(downloadSettings);
    }

    public event EventHandler<IEnumerable<BookItemViewModel>> DownloadRequested;

    public string SelectedBookAsin { get; set; }

    // Sort state remembered within the session
    public int? SortColumnIndex { get; set; }

    public ListSortDirection? SortDirection { get; set; }

    public void LoadBooks(IEnumerable<Book> books)
    {
      Books.Clear();
      foreach (var book in books)
      {
        var vm = new BookItemViewModel(book);
        vm.PropertyChanged += (s, e) =>
        {
          if (e.PropertyName == nameof(BookItemViewModel.IsSelected))
          {
            UpdateSelectedCount();
          }
        };
        Books.Add(vm);
      }

      UpdateSelectedCount();

      if (Books.Count == 0)
      {
        SelectedBook = null;
        return;
      }

      var previousSelection = !string.IsNullOrWhiteSpace(SelectedBookAsin)
        ? Books.FirstOrDefault(b => b.Asin == SelectedBookAsin)
        : null;

      SelectedBook = previousSelection ?? Books[0];
    }

    public IEnumerable<BookItemViewModel> GetSelectedBooks() =>
      Books.Where(b => b.IsSelected);

    public void UpdateSelectedCount() =>
      SelectedCount = Books.Count(b => b.IsSelected);

    public void SetDownloadSettings(IDownloadSettings settings)
    {
      if (ReferenceEquals(downloadSettings, settings))
      {
        return;
      }

      if (downloadSettings is not null)
      {
        downloadSettings.ChangedSettings -= OnDownloadSettingsChanged;
      }

      downloadSettings = settings;

      if (downloadSettings is not null)
      {
        downloadSettings.ChangedSettings += OnDownloadSettingsChanged;
      }

      OpenDownloadsFolderCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedBookChanged(BookItemViewModel value)
    {
      HasSelectedBook = value is not null;
      if (value is not null)
        SelectedBookAsin = value.Asin;
    }

    [RelayCommand]
    private void SelectAll()
    {
      foreach (var book in Books)
      {
        book.IsSelected = true;
      }
    }

    [RelayCommand]
    private void DeselectAll()
    {
      foreach (var book in Books)
      {
        book.IsSelected = false;
      }
    }

    [RelayCommand]
    private void DownloadSelected()
    {
      var selected = GetSelectedBooks().ToList();
      if (selected.Count > 0)
      {
        DownloadRequested?.Invoke(this, selected);
      }
    }

    [RelayCommand(CanExecute = nameof(CanOpenDownloadsFolder))]
    private void OpenDownloadsFolder()
    {
      ShellExecute.Directory(downloadSettings?.DownloadDirectory);
    }

    private bool CanOpenDownloadsFolder() =>
      !string.IsNullOrWhiteSpace(downloadSettings?.DownloadDirectory);

    private void OnDownloadSettingsChanged(object sender, EventArgs e) =>
      OpenDownloadsFolderCommand.NotifyCanExecuteChanged();
  }

  public partial class BookItemViewModel : ObservableObject
  {
    private readonly Book book;

    [ObservableProperty]
    private bool isSelected;

    public BookItemViewModel(Book book)
    {
      this.book = book;
    }

    public Book Book => book;

    public string Asin => book.Asin;

    public string Title => book.Title;

    public string Author => book.Author;

    public string Narrator => book.Narrator;

    public DateTime? PurchaseDate => book.PurchaseDate;

    public DateTime? ReleaseDate => book.ReleaseDate;

    public int? RunTimeLengthSeconds => book.RunTimeLengthSeconds;

    public string CoverImageFile => book.CoverImageFile;

    public EConversionState ConversionState => book.Conversion?.State ?? EConversionState.Unknown;

    public string Duration
    {
      get
      {
        if (RunTimeLengthSeconds is null)
        {
          return null;
        }

        var ts = TimeSpan.FromSeconds(RunTimeLengthSeconds.Value);
        return ts.TotalHours >= 1
          ? $"{(int)ts.TotalHours}h {ts.Minutes:D2}m"
          : $"{ts.Minutes}m";
      }
    }

    // Detail properties
    public string Publisher => book.PublisherName;

    public string Language => book.Language;

    public string Unabridged => book.Unabridged switch
    {
      true => "Yes",
      false => "No",
      _ => null
    };

    public string Series => book.Series?.Count > 0
      ? string.Join(", ", book.Series.Select(s => s.ToString()))
      : null;

    public string ConversionStateText => ConversionState.ToString();

    public int? Parts => book.Components?.Count > 0 ? book.Components.Count : (int?)null;

    public string Description
    {
      get
      {
        var html = book.PublisherSummary;
        if (string.IsNullOrWhiteSpace(html))
        {
          return null;
        }

        // Strip HTML tags and decode common entities
        var text = Regex.Replace(html, "<[^>]+>", " ");
        text = text.Replace("&amp;", "&")
                   .Replace("&lt;", "<")
                   .Replace("&gt;", ">")
                   .Replace("&quot;", "\"")
                   .Replace("&#39;", "'")
                   .Replace("&nbsp;", " ");

        // Collapse whitespace
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text;
      }
    }

    public string CoverImagePath
    {
      get
      {
        var path = book.CoverImageFile;
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
          return path;
        }

        return null;
      }
    }

    public bool HasDetails => true;
  }
}
