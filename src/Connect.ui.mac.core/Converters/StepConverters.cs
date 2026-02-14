using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace BookLibConnect.Core.UI.Mac.Converters {
  /// <summary>
  /// Converts an integer step index to a visibility boolean based on whether
  /// it matches the converter parameter.
  /// </summary>
  public class StepVisibilityConverter : IValueConverter {
    public object Convert (object value, Type targetType, object parameter, CultureInfo culture) {
      if (value is int currentStep && parameter is string paramStr && int.TryParse (paramStr, out int targetStep))
        return currentStep == targetStep;
      return false;
    }

    public object ConvertBack (object value, Type targetType, object parameter, CultureInfo culture) =>
      throw new NotSupportedException ();
  }

  /// <summary>
  /// Converts a 0-based step index to a 1-based display number.
  /// </summary>
  public class OneBasedConverter : IValueConverter {
    public object Convert (object value, Type targetType, object parameter, CultureInfo culture) {
      if (value is int step)
        return step + 1;
      return value;
    }

    public object ConvertBack (object value, Type targetType, object parameter, CultureInfo culture) =>
      throw new NotSupportedException ();
  }
}
