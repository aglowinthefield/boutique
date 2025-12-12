using System.Globalization;
using System.Windows.Data;
using Boutique.Models;

namespace Boutique.Utilities;

/// <summary>
/// Converts DistributionFileType enum values to display strings.
/// </summary>
public class DistributionFileTypeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DistributionFileType fileType)
        {
            return fileType switch
            {
                DistributionFileType.Spid => "SPID",
                DistributionFileType.SkyPatcher => "SkyPatcher",
                DistributionFileType.Esp => "ESP",
                _ => value.ToString() ?? string.Empty
            };
        }
        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string str)
        {
            return str switch
            {
                "SPID" => DistributionFileType.Spid,
                "SkyPatcher" => DistributionFileType.SkyPatcher,
                _ => DistributionFileType.SkyPatcher
            };
        }
        return DistributionFileType.SkyPatcher;
    }
}

