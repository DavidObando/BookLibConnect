using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace Oahu.Core.UI.Avalonia.Converters
{
  public class FilePathToImageConverter : IValueConverter
  {
    public static readonly FilePathToImageConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
      if (value is string path && !string.IsNullOrEmpty(path) && File.Exists(path))
      {
        try
        {
          return new Bitmap(path);
        }
        catch
        {
          return null;
        }
      }

      return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
      throw new NotSupportedException();
    }
  }
}
