using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace AudioQualityChecker
{
    /// <summary>How a themed alert is coloured and which glyph it carries.</summary>
    public enum AlertSeverity
    {
        Error,
        Warning,
        Info,
        Question
    }

    /// <summary>
    /// The app's themed replacement for the Win32 MessageBox. Everything else in AudioAuditor is
    /// skinned (including user-authored custom themes), so an OS dialog in the middle of a flow
    /// reads as a different program — same reasoning as ThemedDialog.cs.
    ///
    /// Use <see cref="Show"/> / <see cref="ShowWarning"/> / <see cref="ShowInfo"/> for
    /// acknowledgements and <see cref="Confirm"/> where a MessageBoxButton.YesNo would have been.
    /// </summary>
    public partial class ErrorDialog : Window
    {
        private bool _confirmed;

        public ErrorDialog(string title, string message, Window? owner = null,
            AlertSeverity severity = AlertSeverity.Error, bool isConfirm = false,
            string confirmLabel = "Yes", string cancelLabel = "No")
        {
            InitializeComponent();

            TitleText.Text = title;
            MessageText.Text = message;
            ApplySeverity(severity);

            if (isConfirm)
            {
                OkButton.Content = confirmLabel;
                CancelButton.Content = cancelLabel;
                CancelButton.Visibility = Visibility.Visible;
            }

            // Assigning Owner throws if that window has never been shown (or is already closing),
            // which is reachable from startup and shutdown paths. An unparented dialog is a far
            // better outcome than an exception raised while reporting an error.
            if (owner != null && owner.IsLoaded && owner.IsVisible)
            {
                try { Owner = owner; }
                catch (InvalidOperationException) { }
            }
        }

        /// <summary>
        /// Each severity is a self-contained palette rather than a theme lookup: an alert has to
        /// stay legible as an alert on every theme, including a user's custom one.
        /// </summary>
        private void ApplySeverity(AlertSeverity severity)
        {
            (string surface, string border, string glyphColor, string title, string body,
             string btnBg, string btnBorder, string btnText, string glyph) palette = severity switch
            {
                AlertSeverity.Warning =>
                    ("#FF1E1808", "#FFAA7722", "#FFFFB020", "#FFFFC65C", "#FFCCBB99",
                     "#FF3A3018", "#FF886622", "#FFDDCCAA", ""),
                AlertSeverity.Info =>
                    ("#FF10161E", "#FF2266AA", "#FF3399FF", "#FF66B3FF", "#FFAABBCC",
                     "#FF18283A", "#FF226688", "#FFAACCDD", ""),
                AlertSeverity.Question =>
                    ("#FF14141A", "#FF555577", "#FFAAAAEE", "#FFCCCCFF", "#FFBBBBCC",
                     "#FF25253A", "#FF555588", "#FFCCCCDD", ""),
                _ =>
                    ("#FF1A1010", "#FFAA2222", "#FFFF4444", "#FFFF6666", "#FFCCAAAA",
                     "#FF3A2020", "#FF882222", "#FFDDAAAA", ""),
            };

            Surface.Background = Freeze(palette.surface);
            Surface.BorderBrush = Freeze(palette.border);
            GlyphText.Foreground = Freeze(palette.glyphColor);
            GlyphText.Text = palette.glyph;
            TitleText.Foreground = Freeze(palette.title);
            MessageText.Foreground = Freeze(palette.body);

            var btnBg = Freeze(palette.btnBg);
            var btnBorder = Freeze(palette.btnBorder);
            var btnText = Freeze(palette.btnText);
            foreach (var btn in new[] { OkButton, CancelButton })
            {
                btn.Background = btnBg;
                btn.BorderBrush = btnBorder;
                btn.Foreground = btnText;
            }
        }

        private static SolidColorBrush Freeze(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        private void Header_MouseDown(object sender, MouseButtonEventArgs e)
        {
            this.SafeDragMove();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            _confirmed = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _confirmed = false;
            Close();
        }

        /// <summary>Shows a themed error dialog with a single OK button.</summary>
        public static void Show(string title, string message, Window? owner = null)
            => ShowSeverity(title, message, owner, AlertSeverity.Error);

        /// <summary>Shows a themed warning dialog with a single OK button.</summary>
        public static void ShowWarning(string title, string message, Window? owner = null)
            => ShowSeverity(title, message, owner, AlertSeverity.Warning);

        /// <summary>Shows a themed informational dialog with a single OK button.</summary>
        public static void ShowInfo(string title, string message, Window? owner = null)
            => ShowSeverity(title, message, owner, AlertSeverity.Info);

        private static void ShowSeverity(string title, string message, Window? owner, AlertSeverity severity)
        {
            var dlg = new ErrorDialog(title, message, owner, severity);
            dlg.ShowDialog();
        }

        /// <summary>
        /// Themed stand-in for MessageBox.Show(..., MessageBoxButton.YesNo). Returns true only
        /// when the confirm button was pressed — closing via Esc or the window chrome is a "no".
        /// </summary>
        public static bool Confirm(string title, string message, Window? owner = null,
            string confirmLabel = "Yes", string cancelLabel = "No",
            AlertSeverity severity = AlertSeverity.Question)
        {
            var dlg = new ErrorDialog(title, message, owner, severity,
                isConfirm: true, confirmLabel: confirmLabel, cancelLabel: cancelLabel);
            dlg.ShowDialog();
            return dlg._confirmed;
        }
    }
}
