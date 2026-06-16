using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Navigation;

namespace AudioQualityChecker
{
    /// <summary>
    /// Read-only "Open-Source Credits &amp; Licenses" viewer reached from Settings → About.
    /// Lists the third-party libraries the desktop app ships with. Keep this list in sync
    /// with the "Credits &amp; Acknowledgments" table in README.md.
    /// </summary>
    public partial class CreditsWindow : Window
    {
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_CAPTION_COLOR = 35;

        private readonly record struct Credit(string Name, string By, string License, string Usage, string Url, string Notice);

        // Shipped desktop runtime libraries only — mirrors README.md. (Build/test tooling,
        // the web build, and experimental cross-platform builds are intentionally excluded.)
        // Notice = the bundled license file name in the Third.Party.Notices folder beside the exe.
        private static readonly Credit[] Credits =
        {
            new("NAudio", "Mark Heath", "MIT",
                "Audio playback, waveform reading, the sample-provider pipeline, FFT analysis, crossfade mixing, and all audio I/O.",
                "https://github.com/naudio/NAudio", "NAudio-LICENSE.txt"),
            new("NAudio.Vorbis", "Andrew Ward", "MIT",
                "OGG Vorbis audio file decoding and playback support.",
                "https://github.com/naudio/Vorbis", "NAudio.Vorbis-LICENSE.txt"),
            new("NLayer", "Mark Heath & Andrew Ward", "MIT",
                "Pure-managed MPEG/MP3 decoder — enables MP3 analysis on Linux/macOS (and as a Windows fallback) where Media Foundation isn't available.",
                "https://github.com/naudio/NLayer", "NLayer-LICENSE.txt"),
            new("Concentus & Concentus.OggFile", "Logan Stromberg", "MIT / BSD",
                "Pure managed Opus audio decoding for .opus file support.",
                "https://github.com/lostromb/concentus", "Concentus-LICENSE.txt"),
            new("TagLib#", "Mono Project", "LGPL-2.1",
                "Reading and writing audio metadata tags across all supported formats (ID3v2, Xiph Comment, APEv2, M4A atoms).",
                "https://github.com/mono/taglib-sharp", "TagLibSharp-LICENSE.txt"),
            new("ClosedXML", "ClosedXML Contributors", "MIT",
                "Excel workbook generation with styled cells, headers, and auto-fit columns.",
                "https://github.com/ClosedXML/ClosedXML", "ClosedXML-LICENSE.txt"),
            new("discord-rpc-csharp", "Lachee", "MIT",
                "Discord Rich Presence client for showing playback status.",
                "https://github.com/Lachee/discord-rpc-csharp", "discord-rpc-csharp-LICENSE.txt"),
            new("SharpCompress", "Adam Hathcock", "MIT",
                "Archive extraction support (ZIP, RAR, 7Z, TAR).",
                "https://github.com/adamhathcock/sharpcompress", "SharpCompress-LICENSE.txt"),
            new("System.Security.Cryptography.ProtectedData", "Microsoft", "MIT",
                "DPAPI wrapper used to encrypt stored scrobbler credentials. Shipped as a NuGet package (part of the .NET runtime).",
                "https://www.nuget.org/packages/System.Security.Cryptography.ProtectedData", "dotnet-runtime-LICENSE.txt"),
            new(".NET 8", "Microsoft", "MIT",
                "Application runtime.",
                "https://github.com/dotnet/runtime", "dotnet-runtime-LICENSE.txt"),
            new("WPF", "Microsoft", "MIT",
                "UI framework — all windows, controls, data binding, styling, and rendering.",
                "https://github.com/dotnet/wpf", "dotnet-wpf-LICENSE.txt"),
        };

        public CreditsWindow()
        {
            InitializeComponent();
            BuildCards();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ApplyThemeTitleBar();
        }

        private void ApplyThemeTitleBar()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;

                bool isLight = Services.ThemeManager.CurrentTheme == "Light";
                int darkMode = isLight ? 0 : 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

                // TryFindResource (not FindResource) so a missing key never throws.
                if (TryFindResource("TitleBarBg") is SolidColorBrush captionBrush)
                {
                    var c = captionBrush.Color;
                    int colorRef = c.R | (c.G << 8) | (c.B << 16);
                    DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref colorRef, sizeof(int));
                }
            }
            catch { }
        }

        private void BuildCards()
        {
            foreach (var credit in Credits)
                CreditsList.Items.Add(BuildCard(credit));
        }

        private FrameworkElement BuildCard(Credit credit)
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 10),
            };
            card.SetResourceReference(Border.BackgroundProperty, "InputBg");
            card.SetResourceReference(Border.BorderBrushProperty, "ButtonBorder");

            var stack = new StackPanel();

            // Title row: library name + license badge.
            var titleRow = new Grid();
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = new TextBlock
            {
                Text = credit.Name,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Segoe UI"),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            };
            name.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
            Grid.SetColumn(name, 0);
            titleRow.Children.Add(name);

            var badge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                BorderThickness = new Thickness(1),
            };
            badge.SetResourceReference(Border.BorderBrushProperty, "AccentColor");
            var badgeText = new TextBlock
            {
                Text = credit.License,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                FontFamily = new FontFamily("Segoe UI"),
            };
            badgeText.SetResourceReference(TextBlock.ForegroundProperty, "AccentColor");
            badge.Child = badgeText;
            Grid.SetColumn(badge, 1);
            titleRow.Children.Add(badge);

            stack.Children.Add(titleRow);

            // "by Author"
            var by = new TextBlock
            {
                Text = "by " + credit.By,
                FontSize = 11,
                FontFamily = new FontFamily("Segoe UI"),
                Margin = new Thickness(0, 3, 0, 0),
            };
            by.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
            stack.Children.Add(by);

            // Usage description
            var usage = new TextBlock
            {
                Text = credit.Usage,
                FontSize = 12,
                FontFamily = new FontFamily("Segoe UI"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
            };
            usage.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            stack.Children.Add(usage);

            // Project link
            var link = new Hyperlink(new Run(credit.Url))
            {
                NavigateUri = new Uri(credit.Url),
                TextDecorations = null,
            };
            link.SetResourceReference(Hyperlink.ForegroundProperty, "AccentColor");
            link.RequestNavigate += Hyperlink_RequestNavigate;
            var linkBlock = new TextBlock
            {
                FontSize = 11,
                FontFamily = new FontFamily("Segoe UI"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            linkBlock.Inlines.Add(link);

            // Row: project link (left) + "View license" button (right) that opens the bundled file.
            var linkRow = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            linkRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            linkRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(linkBlock, 0);
            linkRow.Children.Add(linkBlock);

            var licenseBtn = new Button
            {
                Content = "View license",
                Tag = credit.Notice,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(8, 0, 0, 0),
            };
            if (TryFindResource("LicenseButton") is Style licenseStyle)
                licenseBtn.Style = licenseStyle;
            licenseBtn.Click += LicenseButton_Click;
            Grid.SetColumn(licenseBtn, 1);
            linkRow.Children.Add(licenseBtn);

            stack.Children.Add(linkRow);

            card.Child = stack;
            return card;
        }

        private void LicenseButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string fileName || string.IsNullOrEmpty(fileName))
                return;
            try
            {
                string path = System.IO.Path.Combine(AppContext.BaseDirectory, "Third.Party.Notices", fileName);
                if (System.IO.File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
                else
                {
                    MessageBox.Show(this,
                        "The bundled license file couldn't be found:\n" + fileName,
                        "AudioAuditor", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Couldn't open the license file.\n" + ex.Message,
                    "AudioAuditor", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch { }
            e.Handled = true;
        }

        private void Header_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
