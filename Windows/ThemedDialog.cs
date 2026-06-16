using System;
using System.Windows;
using System.Windows.Input;

namespace AudioQualityChecker
{
    /// <summary>
    /// A small reusable themed modal dialog (confirm, or confirm-with-text-input) backed by the
    /// ThemedDialogOverlay in MainWindow.xaml. Avoids the unstyled Win32 MessageBox so reset
    /// confirmations and the "name this profile" prompt match the active theme / ColorMatch look.
    ///
    /// Usage: ShowThemedConfirm(title, message, onConfirm) or
    ///        ShowThemedInput(title, message, initial, onConfirm: name => ...).
    /// Only one dialog shows at a time; the result is delivered via the onConfirm callback.
    /// </summary>
    public partial class MainWindow
    {
        private Action? _themedDialogConfirm;       // invoked with no args for confirm dialogs
        private Action<string>? _themedDialogInputConfirm; // invoked with the text for input dialogs

        private void ShowThemedConfirm(string title, string message, Action onConfirm, string confirmLabel = "OK")
        {
            _themedDialogConfirm = onConfirm;
            _themedDialogInputConfirm = null;
            ThemedDialogTitle.Text = title;
            ThemedDialogMessage.Text = message;
            ThemedDialogInput.Visibility = Visibility.Collapsed;
            ThemedDialogConfirmBtn.Content = confirmLabel;
            ThemedDialogOverlay.Visibility = Visibility.Visible;
        }

        private void ShowThemedInput(string title, string message, string initial,
            Action<string> onConfirm, string confirmLabel = "Save")
        {
            _themedDialogConfirm = null;
            _themedDialogInputConfirm = onConfirm;
            ThemedDialogTitle.Text = title;
            ThemedDialogMessage.Text = message;
            ThemedDialogInput.Visibility = Visibility.Visible;
            ThemedDialogInput.Text = initial ?? "";
            ThemedDialogConfirmBtn.Content = confirmLabel;
            ThemedDialogOverlay.Visibility = Visibility.Visible;
            ThemedDialogInput.Focus();
            ThemedDialogInput.SelectAll();
        }

        private void HideThemedDialog()
        {
            ThemedDialogOverlay.Visibility = Visibility.Collapsed;
            _themedDialogConfirm = null;
            _themedDialogInputConfirm = null;
        }

        private void ThemedDialogConfirm_Click(object sender, RoutedEventArgs e)
        {
            // Snapshot the callbacks before hiding (Hide clears them).
            var confirm = _themedDialogConfirm;
            var inputConfirm = _themedDialogInputConfirm;
            string text = ThemedDialogInput.Text?.Trim() ?? "";
            HideThemedDialog();

            if (inputConfirm != null)
            {
                if (!string.IsNullOrWhiteSpace(text))
                    inputConfirm(text);
            }
            else
            {
                confirm?.Invoke();
            }
        }

        private void ThemedDialogCancel_Click(object sender, RoutedEventArgs e) => HideThemedDialog();

        private void ThemedDialogBackdrop_Click(object sender, MouseButtonEventArgs e) => HideThemedDialog();

        private void ThemedDialogInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                ThemedDialogConfirm_Click(sender, e);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                HideThemedDialog();
            }
        }
    }
}
