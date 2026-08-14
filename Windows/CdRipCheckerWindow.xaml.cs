using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AudioQualityChecker.Models;
using AudioQualityChecker.Services;

namespace AudioQualityChecker
{
    /// <summary>
    /// CD Rip Checker tool window: drop or browse for an EAC / XLD / whipper rip log and see its
    /// cambia-scored verdict plus the per-deduction breakdown.
    /// </summary>
    public partial class CdRipCheckerWindow : Window
    {
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_CAPTION_COLOR = 35;

        private static readonly Brush GreenBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xC9, 0x4C));
        private static readonly Brush AmberBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x00));
        private static readonly Brush RedBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x5C));

        private readonly string? _autoPath;

        /// <param name="owner">Owning window.</param>
        /// <param name="initialPath">Optional .log file or folder to check on open (e.g. the selected
        /// file's folder). A folder is searched for the first parseable rip log.</param>
        public CdRipCheckerWindow(Window owner, string? initialPath = null)
        {
            InitializeComponent();
            Owner = owner;
            _autoPath = initialPath;
            if (!RipLogCheckService.IsAvailable)
                StatusText.Text = "cambia not found — the checker is unavailable in this build.";
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ApplyThemeTitleBar();

            if (!string.IsNullOrEmpty(_autoPath) && RipLogCheckService.IsAvailable)
                CheckAsync(_autoPath).Observe(nameof(CheckAsync), ex => StatusText.Text = $"Error: {ex.Message}");
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

                if (FindResource("TitleBarBg") is SolidColorBrush captionBrush)
                {
                    var c = captionBrush.Color;
                    int color = c.R | (c.G << 8) | (c.B << 16);
                    DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref color, sizeof(int));
                }
            }
            catch { }
        }

        private void Header_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) this.SafeDragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select a CD rip log",
                Filter = "Rip logs (*.log;*.txt)|*.log;*.txt|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog(this) == true)
                CheckAsync(dlg.FileName).Observe(nameof(CheckAsync), ex => StatusText.Text = $"Error: {ex.Message}");
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
                CheckAsync(paths[0]).Observe(nameof(CheckAsync), ex => StatusText.Text = $"Error: {ex.Message}");
        }

        private async Task CheckAsync(string path)
        {
            bool isFolder = System.IO.Directory.Exists(path);
            PathText.Text = path;
            PathText.Foreground = (Brush)FindResource("TextPrimary");
            ResultsPanel.Children.Clear();
            StatusText.Text = "Checking…";

            RipLogResult? result;
            try
            {
                result = isFolder
                    ? await RipLogCheckService.CheckFolderAsync(path)
                    : await RipLogCheckService.CheckLogAsync(path);
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error: {ex.Message}";
                return;
            }

            StatusText.Text = "";
            if (result == null)
            {
                ResultsPanel.Children.Add(new TextBlock
                {
                    Text = "No EAC / XLD / whipper rip log found in that folder.",
                    Foreground = AmberBrush,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0)
                });
                return;
            }
            Render(result);
        }

        private void Render(RipLogResult result)
        {
            if (!result.IsParsed)
            {
                ResultsPanel.Children.Add(new TextBlock
                {
                    Text = result.Error,
                    Foreground = AmberBrush,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0)
                });
                return;
            }

            Brush verdictBrush = result.Verdict switch
            {
                "Perfect" or "Good" => GreenBrush,
                "Suspect" => AmberBrush,
                _ => RedBrush
            };

            ResultsPanel.Children.Add(new TextBlock
            {
                Text = $"{result.Score} / 100",
                Foreground = verdictBrush,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 30,
                FontWeight = FontWeights.Bold
            });
            ResultsPanel.Children.Add(new TextBlock
            {
                Text = result.Verdict,
                Foreground = verdictBrush,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            AddInfo("Ripper", $"{result.Ripper} {result.RipperVersion}".Trim());
            AddInfo("Drive", result.Drive);

            ResultsPanel.Children.Add(new Separator { Margin = new Thickness(0, 10, 0, 8) });

            if (result.Deductions.Count == 0)
            {
                ResultsPanel.Children.Add(MakeRow("No deductions — clean log.", GreenBrush, ""));
                return;
            }

            ResultsPanel.Children.Add(new TextBlock
            {
                Text = "Findings",
                Foreground = (Brush)FindResource("TextSecondary"),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            });

            foreach (var d in result.Deductions)
            {
                Brush c = d.Class switch
                {
                    "Critical" or "Bad" => RedBrush,
                    "Neutral" => (Brush)FindResource("TextMuted"),
                    _ => GreenBrush
                };
                ResultsPanel.Children.Add(MakeRow(d.Message, c, d.Score));
            }
        }

        private void AddInfo(string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            ResultsPanel.Children.Add(new TextBlock
            {
                Text = $"{label}: {value}",
                Foreground = (Brush)FindResource("TextSecondary"),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12,
                Margin = new Thickness(0, 1, 0, 1)
            });
        }

        private UIElement MakeRow(string message, Brush color, string score)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            if (!string.IsNullOrEmpty(score) && score != "0")
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"[{score}] ",
                    Foreground = color,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            panel.Children.Add(new TextBlock
            {
                Text = message,
                Foreground = color,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 420,
                VerticalAlignment = VerticalAlignment.Center
            });
            return panel;
        }
    }
}
