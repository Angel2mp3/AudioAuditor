using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AudioQualityChecker.Services;

namespace AudioQualityChecker
{
    public partial class MainWindow
    {
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_CAPTION_COLOR = 35;

        private void ApplyThemeTitleBar()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;

                bool isLight = ThemeManager.CurrentTheme == "Light";
                int darkMode = isLight ? 0 : 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

                int colorRef;
                var titleColor = default(Color);
                if (_npVisible && TryResolveBrushColor(NpBottomBar?.Background, out var npBottomColor))
                    titleColor = npBottomColor;
                else if (_npVisible && TryResolveBrushColor(FindResource("GlassToolbarBg") as Brush, out var npToolbarColor))
                    titleColor = npToolbarColor;
                else if (ThemeManager.MainColorMatchEnabled && _mainAlbumPrimary != default)
                {
                    titleColor = Color.FromRgb(
                        (byte)Math.Max(18, _mainAlbumPrimary.R / 4),
                        (byte)Math.Max(18, _mainAlbumPrimary.G / 4),
                        (byte)Math.Max(18, _mainAlbumPrimary.B / 4));
                }

                if (titleColor != default)
                    colorRef = titleColor.R | (titleColor.G << 8) | (titleColor.B << 16);
                else
                    colorRef = ThemeManager.GetTitleBarColorRef();

                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref colorRef, sizeof(int));
            }
            catch { }
        }

        private static bool TryResolveBrushColor(Brush? brush, out Color color)
        {
            if (brush is SolidColorBrush solid)
            {
                color = solid.Color;
                return true;
            }

            if (brush is LinearGradientBrush gradient && gradient.GradientStops.Count > 0)
            {
                double weightTotal = 0;
                double r = 0;
                double g = 0;
                double b = 0;
                foreach (var stop in gradient.GradientStops)
                {
                    double weight = Math.Max(1.0, stop.Color.A);
                    r += stop.Color.R * weight;
                    g += stop.Color.G * weight;
                    b += stop.Color.B * weight;
                    weightTotal += weight;
                }

                color = Color.FromRgb(
                    (byte)Math.Clamp((int)Math.Round(r / weightTotal), 0, 255),
                    (byte)Math.Clamp((int)Math.Round(g / weightTotal), 0, 255),
                    (byte)Math.Clamp((int)Math.Round(b / weightTotal), 0, 255));
                return true;
            }

            color = default;
            return false;
        }

        internal void ApplyMainCustomBackground()
        {
            try
            {
                string path = ThemeManager.MainBackgroundImagePath;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    MainBackgroundImage.Source = null;
                    MainBackgroundImage.Opacity = 0;
                    return;
                }

                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.EndInit();
                image.Freeze();

                MainBackgroundBlurEffect.Radius = Math.Clamp(ThemeManager.MainBackgroundBlur, 0, 48);
                MainBackgroundImage.Source = image;
                MainBackgroundImage.Opacity = Math.Clamp(ThemeManager.MainBackgroundOpacity, 0, 0.8);
            }
            catch
            {
                MainBackgroundImage.Source = null;
                MainBackgroundImage.Opacity = 0;
            }
        }

        private static Color BlendTitleBarColor(Color primary, Color secondary, Color background)
        {
            byte Mix(byte a, byte b, byte c)
            {
                int value = (int)(a * 0.24 + b * 0.16 + c * 0.18);
                return (byte)Math.Clamp(value, 18, 72);
            }

            if (secondary == default) secondary = primary;
            if (background == default) background = primary;

            return Color.FromRgb(
                Mix(primary.R, secondary.R, background.R),
                Mix(primary.G, secondary.G, background.G),
                Mix(primary.B, secondary.B, background.B));
        }
    }
}
