using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace AudioQualityChecker.Windows;

public partial class WelcomeDialog : Window
{
    public bool SelectedOffline { get; private set; } = false;

    // Feature choices
    public bool EnableSilenceDetection { get; private set; } = false;
    public bool EnableFakeStereoDetection { get; private set; } = true;
    public bool EnableDynamicRange { get; private set; } = false;
    public bool EnableTruePeak { get; private set; } = false;
    public bool EnableLufs { get; private set; } = false;
    public bool EnableClippingDetection { get; private set; } = true;
    public bool EnableBpmDetection { get; private set; } = false;
    public bool EnableMqaDetection { get; private set; } = true;
    public bool EnableDefaultAiDetection { get; private set; } = true;
    public bool EnableExperimentalAi { get; private set; } = false;
    public bool EnableRipQuality { get; private set; } = false;
    public bool EnableSHLabs { get; private set; } = false;

    public WelcomeDialog()
    {
        InitializeComponent();
        UpdateSelectionVisuals();
    }

    private void OnlinePanel_Click(object sender, MouseButtonEventArgs e)
    {
        SelectedOffline = false;
        UpdateSelectionVisuals();
    }

    private void OfflinePanel_Click(object sender, MouseButtonEventArgs e)
    {
        SelectedOffline = true;
        UpdateSelectionVisuals();
    }

    private void UpdateSelectionVisuals()
    {
        if (!SelectedOffline)
        {
            // Online selected
            OnlinePanelBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x88, 0xFF));
            OnlinePanelBorder.BorderThickness = new Thickness(2);
            OnlinePanelBorder.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2A));

            OfflinePanelBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D));
            OfflinePanelBorder.BorderThickness = new Thickness(1.5);
            OfflinePanelBorder.Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18));
        }
        else
        {
            // Offline selected
            OnlinePanelBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D));
            OnlinePanelBorder.BorderThickness = new Thickness(1.5);
            OnlinePanelBorder.Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18));

            OfflinePanelBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x88, 0xFF));
            OfflinePanelBorder.BorderThickness = new Thickness(2);
            OfflinePanelBorder.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2A));
        }
    }

    private void BtnGetStarted_Click(object sender, RoutedEventArgs e)
    {
        // Capture feature choices
        EnableSilenceDetection = ChkSilence.IsChecked == true;
        EnableFakeStereoDetection = ChkFakeStereo.IsChecked == true;
        EnableDynamicRange = ChkDR.IsChecked == true;
        EnableTruePeak = ChkTruePeak.IsChecked == true;
        EnableLufs = ChkLufs.IsChecked == true;
        EnableClippingDetection = ChkClipping.IsChecked == true;
        EnableBpmDetection = ChkBpm.IsChecked == true;
        EnableMqaDetection = ChkMqa.IsChecked == true;
        EnableDefaultAiDetection = ChkDefaultAi.IsChecked == true;
        EnableExperimentalAi = ChkExperimentalAi.IsChecked == true;
        EnableRipQuality = ChkRipQuality.IsChecked == true;
        EnableSHLabs = ChkSHLabs.IsChecked == true;

        DialogResult = true;
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }
}
