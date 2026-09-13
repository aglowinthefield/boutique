using System.Globalization;
using System.Windows.Data;
using Boutique.Resources;

namespace Boutique.Views;

/// <summary>
///   Formats a localized composite-format string with one or more values.
///   The format string is NOT passed through the binding pipeline (markup extensions cannot
///   appear inside a MultiBinding's child collection, which only accepts BindingBase).
///   Instead <paramref name="parameter" /> carries pipe-separated resource keys:
///   the first segment is the format key; remaining segments are context-dependent:
///   <list type="bullet">
///     <item>When used as IMultiValueConverter with bindings, segment 2 is the optional
///       "empty state" key returned when every bound value is null or empty.</item>
///     <item>When used as IValueConverter with a null-source binding (no bound values),
///       segments 2.. are resource-key arguments, each resolved against the resource table
///       (segments that resolve to no resource are treated as literal text).</item>
///   </list>
/// </summary>
public class FormatConverter : IValueConverter, IMultiValueConverter
{
  private static string? ResolveSegment(string segment) =>
    Strings.ResourceManager.GetString(segment, Strings.Culture) ?? segment;

  public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    if (parameter is not string param)
    {
      return string.Empty;
    }

    var segments = param.Split('|');
    var format = ResolveSegment(segments[0]);

    if (segments.Length == 1)
    {
      return format;
    }

    var args = new object[segments.Length - 1];
    for (var i = 1; i < segments.Length; i++)
    {
      args[i - 1] = ResolveSegment(segments[i]);
    }

    return TryFormat(format, args);
  }

  public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
    throw new NotSupportedException();

  public object Convert(object?[]? values, Type targetType, object? parameter, CultureInfo culture)
  {
    if (parameter is not string param)
    {
      return string.Empty;
    }

    var segments = param.Split('|');
    var format = ResolveSegment(segments[0]);

    if (values == null || values.Length == 0)
    {
      return format;
    }

    if (values.All(v => v is null or string { Length: 0 }))
    {
      return segments.Length > 1
               ? Strings.ResourceManager.GetString(segments[1], Strings.Culture) ?? string.Empty
               : string.Empty;
    }

    return TryFormat(format, values);
  }

  public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
    throw new NotSupportedException();

  private static object TryFormat(string format, object?[] args)
  {
    try
    {
      return string.Format(CultureInfo.CurrentCulture, format, args);
    }
    catch (FormatException)
    {
      return format;
    }
  }
}
