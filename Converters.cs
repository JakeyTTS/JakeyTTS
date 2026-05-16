using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JakeyTTS.Melodies; // Asegúrate de que esto apunte a donde está MelodyPoint
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;

namespace JakeyTTS.Converters
{
    // Para las Tarjetas de Voces Mezcladas
    public class PointCountConverterVoice : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is int count)
            {
                return count == 1 ? "1 Voice component" : $"{count} Voices combined";
            }
            return "0 Voices";
        }
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    // Para el Porcentaje de los Sliders (0.0 - 1.0 -> 0% - 100%)
    public class PercentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            try
            {
                float val = System.Convert.ToSingle(value);
                return $"{(val * 100):N0}%";
            }
            catch { return "0%"; }
        }
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    // --- Otros conversores existentes ---

    public class PointCountConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, string l) => $"{((ICollection<MelodyPoint>)v).Count} points";
        public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
    }

    public class TagPreviewConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, string l) => $"[melody:{v}] / [mix:{v}";
        public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
    }

    public class TagVoicePreviewConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, string l) => $"[voice:{v}]";
        public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
    }

    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool isConditionMet;

            if (value is bool b)
                isConditionMet = b;
            else if (value is string str)
                isConditionMet = string.IsNullOrWhiteSpace(str);
            else if (value is int i)
                isConditionMet = i == 0;
            else
                isConditionMet = (value == null); // For SelectedItem

            bool isInverse = parameter?.ToString() == "Inverse";

            if (isInverse)
                return isConditionMet ? Visibility.Collapsed : Visibility.Visible;
            else
                return isConditionMet ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public class MelodyToPointCollectionConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is IEnumerable<MelodyPoint> points && points.Any())
            {
                var pc = new PointCollection();
                // Sort points by time to ensure the line draws from left to right
                var sorted = points.OrderBy(p => p.TimePct).ToList();

                foreach (var p in sorted)
                {
                    // Map Time (0-1) to X (0-100)
                    double x = p.TimePct * 100;

                    // Map Pitch (0.5-2.0) to Y. 
                    // In UI, 0 is top and 40 is bottom.
                    // Higher pitch should be higher up (smaller Y).
                    double y = 40 - ((p.Pitch - 0.5) / (2.0 - 0.5) * 40);

                    pc.Add(new Point(x, y));
                }
                return pc;
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }

    public class Base64ToImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            string base64 = value as string;
            if (string.IsNullOrWhiteSpace(base64)) return null;

            try
            {
                // Remove the "data:image/png;base64," prefix if it exists
                if (base64.Contains(",")) base64 = base64.Split(',')[1];

                byte[] bytes = System.Convert.FromBase64String(base64);
                using var ms = new MemoryStream(bytes);
                var image = new BitmapImage();
                // RandomAccessStream is needed for WinUI 3, we wrap the memory stream
                image.SetSource(ms.AsRandomAccessStream());
                return image;
            }
            catch { return null; }
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }


}