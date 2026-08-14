using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using AudioQualityChecker.Models;

namespace AudioQualityChecker.Converters
{
    public class StatusToColorConverter : IValueConverter
    {
        // Bound to the status column, so this runs for every visible row on every grid refresh.
        // Allocating a fresh unfrozen brush per call churned the heap and denied WPF the
        // freezable fast path — the other converters in this file already do it this way.
        private static readonly SolidColorBrush Valid = new(Color.FromRgb(76, 175, 80));
        private static readonly SolidColorBrush Fake = new(Color.FromRgb(244, 67, 54));
        private static readonly SolidColorBrush Unknown = new(Color.FromRgb(255, 152, 0));
        private static readonly SolidColorBrush Corrupt = new(Color.FromRgb(156, 39, 176));
        private static readonly SolidColorBrush Optimized = new(Color.FromRgb(255, 193, 7));
        private static readonly SolidColorBrush Analyzing = new(Color.FromRgb(100, 100, 100));
        private static readonly SolidColorBrush None = new(Colors.Transparent);

        static StatusToColorConverter()
        {
            Valid.Freeze(); Fake.Freeze(); Unknown.Freeze(); Corrupt.Freeze();
            Optimized.Freeze(); Analyzing.Freeze(); None.Freeze();
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is AudioStatus status)
            {
                return status switch
                {
                    AudioStatus.Valid => Valid,
                    AudioStatus.Fake => Fake,
                    AudioStatus.Unknown => Unknown,
                    AudioStatus.Corrupt => Corrupt,
                    AudioStatus.Optimized => Optimized,
                    AudioStatus.Analyzing => Analyzing,
                    _ => None
                };
            }
            return None;
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

    public class ClippingToColorConverter : IMultiValueConverter
    {
        private static readonly SolidColorBrush Red = new(Color.FromRgb(244, 67, 54));
        private static readonly SolidColorBrush YellowOrange = new(Color.FromRgb(255, 183, 77));
        private static readonly SolidColorBrush Default = new(Color.FromRgb(212, 212, 212));

        static ClippingToColorConverter()
        {
            Red.Freeze(); YellowOrange.Freeze(); Default.Freeze();
        }

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            bool hasClipping = values.Length > 0 && values[0] is bool b1 && b1;
            bool hasScaledClipping = values.Length > 1 && values[1] is bool b2 && b2;

            if (hasClipping) return Red;
            if (hasScaledClipping) return YellowOrange;
            return Default;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
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
        private static readonly SolidColorBrush MqaBlue = new(Color.FromRgb(30, 144, 255));
        private static readonly SolidColorBrush NoMqa = new(Color.FromRgb(212, 212, 212));

        static MqaToColorConverter() { MqaBlue.Freeze(); NoMqa.Freeze(); }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isMqa && isMqa)
                return MqaBlue;
            return NoMqa;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class StarConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? "★" : "☆";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class AiToColorConverter : IValueConverter
    {
        private static readonly SolidColorBrush AiYes = new(Color.FromRgb(255, 87, 34));      // Deep orange
        private static readonly SolidColorBrush AiPossible = new(Color.FromRgb(255, 193, 7));  // Amber/yellow
        private static readonly SolidColorBrush NotAi = new(Color.FromRgb(212, 212, 212));

        static AiToColorConverter() { AiYes.Freeze(); AiPossible.Freeze(); NotAi.Freeze(); }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Accept either the new three-state verdict string or a legacy bool
            if (value is string verdict)
            {
                return verdict switch
                {
                    "Yes" => AiYes,
                    "Possible" => AiPossible,
                    _ => NotAi,
                };
            }
            if (value is bool isAi && isAi)
                return AiYes;
            return NotAi;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
