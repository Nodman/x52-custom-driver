using System;
using System.Windows.Data;

namespace X52.CustomDriver.App
{
    public class ModeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is int mode)
            {
                return mode == 0 ? "Any Mode" : $"Mode {mode}";
            }
            return "Unknown";
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return 0; // Not used
        }
    }
}
