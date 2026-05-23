using Microsoft.UI.Xaml.Data;
using System;

namespace JakeyTTS.Core.Converters
{
    public class EnumToVisConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value == null || parameter == null) return Microsoft.UI.Xaml.Visibility.Collapsed;
            string valStr = value.ToString();
            string[] parts = parameter.ToString().Split(',');
            foreach (var part in parts)
            {
                if (valStr.Equals(part.Trim(), StringComparison.OrdinalIgnoreCase))
                    return Microsoft.UI.Xaml.Visibility.Visible;
            }
            return Microsoft.UI.Xaml.Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public class PluginToVisConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value == null || string.IsNullOrWhiteSpace(value.ToString()) || value.ToString() == "None") 
                return Microsoft.UI.Xaml.Visibility.Collapsed;
            return Microsoft.UI.Xaml.Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }
    public class BoolToVisConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool b && b) return Microsoft.UI.Xaml.Visibility.Visible;
            return Microsoft.UI.Xaml.Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public class InverseBoolToVisConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool b && b) return Microsoft.UI.Xaml.Visibility.Collapsed;
            return Microsoft.UI.Xaml.Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }
}
