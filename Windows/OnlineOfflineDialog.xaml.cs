using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace AudioQualityChecker.Windows;

public partial class OnlineOfflineDialog : Window
{
    public bool SelectedOffline { get; private set; } = false;

    public OnlineOfflineDialog()
    {
        InitializeComponent();
    }

    private void BtnOnline_Click(object sender, RoutedEventArgs e) => ChooseOnline();
    private void BtnOffline_Click(object sender, RoutedEventArgs e) => ChooseOffline();
    private void OnlinePanel_Click(object sender, MouseButtonEventArgs e) => ChooseOnline();
    private void OfflinePanel_Click(object sender, MouseButtonEventArgs e) => ChooseOffline();

    private void ChooseOnline()
    {
        SelectedOffline = false;
        DialogResult = true;
    }

    private void ChooseOffline()
    {
        SelectedOffline = true;
        DialogResult = true;
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }
}
