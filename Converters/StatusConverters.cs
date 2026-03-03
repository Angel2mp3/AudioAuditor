using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using AudioQualityChecker.Models;

namespace AudioQualityChecker.Converters
{
    public class StatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is AudioStatus status)
            {
                return status switch
                {
                    AudioStatus.Valid => new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                    AudioStatus.Fake => new SolidColorBrush(Color.FromRgb(244, 67, 54)),
                    AudioStatus.Unknown => new SolidColorBrush(Color.FromRgb(255, 152, 0)),
                    AudioStatus.Corrupt => new SolidColorBrush(Color.FromRgb(156, 39, 176)),
                    AudioStatus.Optimized => new SolidColorBrush(Color.FromRgb(255, 193, 7)),
                    AudioStatus.Analyzing => new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                    _ => new SolidColorBrush(Colors.Transparent)
                };
            }
            return new SolidColorBrush(Colors.Transparent);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class StatusToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is AudioStatus status)
            {
                return status switch
                {
                    AudioStatus.Valid => "REAL",
                    AudioStatus.Fake => "FAKE",
                    AudioStatus.Unknown => "UNKNOWN",
                    AudioStatus.Corrupt => "CORRUPTED",
                    AudioStatus.Optimized => "OPTIMIZED",
                    AudioStatus.Analyzing => "...",
                    _ => ""
                };
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class ClippingToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool hasClipping && hasClipping)
                return new SolidColorBrush(Color.FromRgb(244, 67, 54));
            return new SolidColorBrush(Color.FromRgb(212, 212, 212));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Multi-value converter for bitrate columns.
    /// Compares ReportedBitrate and ActualBitrate to determine color:
    ///   Green = matching (valid), Red = far apart (fake), Orange = somewhat off (unknown/corrupt)
    /// </summary>
    public class BitrateToColorConverter : IMultiValueConverter
    {
        private static readonly SolidColorBrush Green = new(Color.FromRgb(76, 175, 80));
        private static readonly SolidColorBrush Red = new(Color.FromRgb(244, 67, 54));
        private static readonly SolidColorBrush Orange = new(Color.FromRgb(255, 152, 0));
        private static readonly SolidColorBrush Default = new(Color.FromRgb(212, 212, 212));

        static BitrateToColorConverter()
        {
            Green.Freeze(); Red.Freeze(); Orange.Freeze(); Default.Freeze();
        }

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2) return Default;
            if (values[0] is not int reported || values[1] is not int actual)
                return Default;

            if (reported <= 0 || actual <= 0) return Default;

            double ratio = (double)actual / reported;

            if (ratio >= 0.80)
                return Green;   // Matching — valid
            else if (ratio >= 0.50)
                return Orange;  // Somewhat off — unknown territory
            else
                return Red;     // Way off — fake
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class MqaToColorConverter : IValueConverter
    {
        private static readonly SolidColorBrush MqaGreen = new(Color.FromRgb(76, 175, 80));
        private static readonly SolidColorBrush MqaBlue = new(Color.FromRgb(30, 144, 255));
        private static readonly SolidColorBrush NoMqa = new(Color.FromRgb(212, 212, 212));

        static MqaToColorConverter() { MqaGreen.Freeze(); MqaBlue.Freeze(); NoMqa.Freeze(); }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isMqa && isMqa)
                return MqaBlue;
            return NoMqa;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class AiToColorConverter : IValueConverter
    {
        private static readonly SolidColorBrush AiDetected = new(Color.FromRgb(255, 87, 34));   // Deep orange
        private static readonly SolidColorBrush NotAi = new(Color.FromRgb(212, 212, 212));

        static AiToColorConverter() { AiDetected.Freeze(); NotAi.Freeze(); }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isAi && isAi)
                return AiDetected;
            return NotAi;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
