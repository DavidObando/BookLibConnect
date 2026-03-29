using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Oahu.Core.UI.Avalonia.Views
{
  /// <summary>
  /// Displays application metadata and project links.
  /// </summary>
  public partial class AboutView : UserControl
  {
    private static readonly Uri RepositoryUri = new("https://github.com/davidobando/oahu");

    /// <summary>
    /// Initializes a new instance of the <see cref="AboutView"/> class.
    /// </summary>
    public AboutView()
    {
      InitializeComponent();
    }

    private static void OpenRepositoryLink(object sender, RoutedEventArgs e)
    {
      Process.Start(new ProcessStartInfo
      {
        FileName = RepositoryUri.AbsoluteUri,
        UseShellExecute = true,
      });
    }
  }
}
