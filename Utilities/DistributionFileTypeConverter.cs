using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows.Data;

namespace Boutique.Utilities;

/// <summary>
///   Converts enum values to their [Description] attribute strings.
///   Falls back to ToString() if no description is defined.
/// </summary>
public class DistributionFileTypeConverter : IValueConverter
{
  public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    if (value is not Enum enumValue)
    {
      return string.Empty;
    }

    var field = enumValue.GetType().GetField(enumValue.ToString());
    var attr  = field?.GetCustomAttribute<DescriptionAttribute>();
    return attr?.Description ?? enumValue.ToString();
  }

  public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
    throw new NotSupportedException("ConvertBack is not supported for enum descriptions");
}
