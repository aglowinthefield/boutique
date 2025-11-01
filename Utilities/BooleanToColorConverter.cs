using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace RequiemGlamPatcher.Views;

public class BooleanToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue && boolValue)
        {
            // Green for MO2 detected
            return new SolidColorBrush(Colors.Green);
        }

        // Gray for other detection methods
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
