using System.Windows;
using System.Windows.Input;

namespace AudioQualityChecker
{
    public partial class ErrorDialog : Window
    {
        public ErrorDialog(string title, string message, Window? owner = null)
        {
            InitializeComponent();

            TitleText.Text = title;
            MessageText.Text = message;

            if (owner != null)
                Owner = owner;
        }

        private void Header_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// Static helper to show a dark/red error dialog.
        /// </summary>
        public static void Show(string title, string message, Window? owner = null)
        {
            var dlg = new ErrorDialog(title, message, owner);
            dlg.ShowDialog();
        }
    }
}
