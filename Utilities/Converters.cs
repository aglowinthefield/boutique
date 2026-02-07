using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Boutique.Utilities;

/// <summary>
///   Converts a boolean to Visibility, returning Collapsed when true (inverse of BooleanToVisibilityConverter).
/// </summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
  public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
    value is true ? Visibility.Collapsed : Visibility.Visible;

  public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
    value is Visibility.Collapsed;
}

/// <summary>
///   Converts a collection count to Visibility. Visible when count > 0, Collapsed otherwise.
///   Use ConverterParameter="Inverse" to invert behavior.
/// </summary>
public class CollectionCountToVisibilityConverter : IValueConverter
{
  public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    var count = value switch
    {
      int i => i,
      ICollection c => c.Count,
      _ => 0
    };

    var isVisible = count > 0;
    if (parameter is "Inverse")
    {
      isVisible = !isVisible;
    }

    return isVisible ? Visibility.Visible : Visibility.Collapsed;
  }

  public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
    throw new NotSupportedException();
}

/// <summary>
///   Converts a string to Visibility. Visible when not null/empty, Collapsed otherwise.
///   Use ConverterParameter="Inverse" to show when empty.
/// </summary>
public class StringEmptyToVisibilityConverter : IValueConverter
{
  public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    var isEmpty = string.IsNullOrEmpty(value as string);
    var isVisible = parameter is "Inverse" ? isEmpty : !isEmpty;
    return isVisible ? Visibility.Visible : Visibility.Collapsed;
  }

  public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
    throw new NotSupportedException();
}

/// <summary>
///   Converts null to Visibility. Visible when not null, Collapsed when null.
///   Use ConverterParameter="Inverse" to invert behavior.
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
  public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    var isNull = value is null;
    var isVisible = parameter is "Inverse" ? isNull : !isNull;
    return isVisible ? Visibility.Visible : Visibility.Collapsed;
  }

  public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
    throw new NotSupportedException();
}

/// <summary>
///   Converts a boolean to its inverse.
/// </summary>
public class InverseBoolConverter : IValueConverter
{
  public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
    value is not true;

  public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
    value is not true;
}

/// <summary>
///   Returns Visibility.Visible when value equals parameter, Collapsed otherwise.
/// </summary>
public class EqualityToVisibilityConverter : IValueConverter
{
  public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
    Equals(value?.ToString(), parameter?.ToString()) ? Visibility.Visible : Visibility.Collapsed;

  public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
    throw new NotSupportedException();
}

/// <summary>
///   Returns Visibility.Collapsed when value equals parameter, Visible otherwise.
/// </summary>
public class InequalityToVisibilityConverter : IValueConverter
{
  public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
    Equals(value?.ToString(), parameter?.ToString()) ? Visibility.Collapsed : Visibility.Visible;

  public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
    throw new NotSupportedException();
}

/// <summary>
///   Returns true if the string is not null/empty/whitespace, false otherwise.
/// </summary>
public class StringNotEmptyConverter : IValueConverter
{
  public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
    !string.IsNullOrWhiteSpace(value as string);

  public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
    throw new NotSupportedException();
}

/// <summary>
///   Returns true if the collection contains the parameter value, false otherwise.
/// </summary>
public class CollectionContainsConverter : IValueConverter
{
  public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    if (value is not IEnumerable collection || parameter is null)
    {
      return false;
    }

    var parameterString = parameter.ToString();
    foreach (var item in collection)
    {
      if (string.Equals(item?.ToString(), parameterString, StringComparison.Ordinal))
      {
        return true;
      }
    }

    return false;
  }

  public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
    throw new NotSupportedException();
}
