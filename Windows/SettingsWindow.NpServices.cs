using System.Windows;
using System.Windows.Controls;
using AudioQualityChecker.Services;
using Microsoft.Win32;

namespace AudioQualityChecker
{
    // Now Playing "Look up this song" service configuration. Independent of the main
    // window's service slots (ThemeManager.NpSearchServiceSlots et al). The rows are
    // built in code rather than hand-duplicated in XAML to keep the 6-slot layout in
    // one place — mirrors the main-window slot UI (combo + Show + Custom URL/icon).
    public partial class SettingsWindow
    {
        private readonly ComboBox[] _npServiceCombos = new ComboBox[6];
        private readonly CheckBox[] _npServiceVisibleChecks = new CheckBox[6];
        private readonly StackPanel[] _npCustomPanels = new StackPanel[6];
        private readonly TextBox[] _npCustomUrlBoxes = new TextBox[6];
        private readonly TextBox[] _npCustomIconBoxes = new TextBox[6];

        /// <summary>Builds the 6 NP service rows and binds them to ThemeManager. Runs while _initializing is true.</summary>
        private void InitNpSearchServiceControls()
        {
            NpSearchServicesContainer.Children.Clear();

            for (int i = 0; i < 6; i++)
            {
                int idx = i; // capture for handlers

                // ── Row: "Slot N:" | service combo | [Show] ──
                var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var label = new TextBlock
                {
                    Text = $"Slot {i + 1}:",
                    FontSize = 12,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary")
                };
                Grid.SetColumn(label, 0);
                row.Children.Add(label);

                var combo = new ComboBox { FontSize = 12, Tag = idx };
                foreach (var svc in ThemeManager.AvailableMusicServices)
                    combo.Items.Add(svc);
                combo.SelectedItem = ThemeManager.NpSearchServiceSlots[idx];
                combo.SelectionChanged += NpServiceCombo_Changed;
                Grid.SetColumn(combo, 1);
                row.Children.Add(combo);
                _npServiceCombos[idx] = combo;

                var show = new CheckBox
                {
                    Content = "Show",
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary"),
                    FontSize = 12,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                    IsChecked = ThemeManager.NpSearchServiceSlotVisible[idx]
                };
                show.Checked += NpServiceVisible_Changed;
                show.Unchecked += NpServiceVisible_Changed;
                Grid.SetColumn(show, 2);
                row.Children.Add(show);
                _npServiceVisibleChecks[idx] = show;

                NpSearchServicesContainer.Children.Add(row);

                // ── Custom URL/icon panel (shown only when slot == "Custom...") ──
                var panel = new StackPanel { Margin = new Thickness(20, 0, 0, 8) };

                var urlRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
                urlRow.Children.Add(new TextBlock
                {
                    Text = "URL:", FontSize = 11, Width = 40,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                    Foreground = (System.Windows.Media.Brush)FindResource("TextMuted"),
                    VerticalAlignment = VerticalAlignment.Center
                });
                var urlBox = new TextBox
                {
                    Width = 340, FontSize = 11, Padding = new Thickness(4, 3, 4, 3), Tag = idx,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                    Background = (System.Windows.Media.Brush)FindResource("InputBg"),
                    Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary"),
                    BorderBrush = (System.Windows.Media.Brush)FindResource("ButtonBorder"),
                    Text = ThemeManager.NpSearchCustomServiceUrls[idx],
                    ToolTip = "Search URL — song name will be appended"
                };
                urlBox.TextChanged += NpCustomUrl_Changed;
                urlRow.Children.Add(urlBox);
                panel.Children.Add(urlRow);
                _npCustomUrlBoxes[idx] = urlBox;

                var iconRow = new StackPanel { Orientation = Orientation.Horizontal };
                iconRow.Children.Add(new TextBlock
                {
                    Text = "Icon:", FontSize = 11, Width = 40,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                    Foreground = (System.Windows.Media.Brush)FindResource("TextMuted"),
                    VerticalAlignment = VerticalAlignment.Center
                });
                var iconBox = new TextBox
                {
                    Width = 280, FontSize = 11, Padding = new Thickness(4, 3, 4, 3), IsReadOnly = true,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                    Background = (System.Windows.Media.Brush)FindResource("InputBg"),
                    Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary"),
                    BorderBrush = (System.Windows.Media.Brush)FindResource("ButtonBorder"),
                    Text = ThemeManager.NpSearchCustomServiceIcons[idx]
                };
                iconRow.Children.Add(iconBox);
                _npCustomIconBoxes[idx] = iconBox;
                var browse = new Button
                {
                    Content = "Browse...", Margin = new Thickness(4, 0, 0, 0),
                    Style = (Style)FindResource("SmallButton"), Tag = idx
                };
                browse.Click += NpBrowseIcon_Click;
                iconRow.Children.Add(browse);
                panel.Children.Add(iconRow);

                NpSearchServicesContainer.Children.Add(panel);
                _npCustomPanels[idx] = panel;
            }

            UpdateNpCustomPanelVisibility();
        }

        private void UpdateNpCustomPanelVisibility()
        {
            for (int i = 0; i < 6; i++)
            {
                bool isCustom = _npServiceCombos[i].SelectedItem as string == "Custom...";
                _npCustomPanels[i].Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void NpServiceCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_initializing) return;
            for (int i = 0; i < 6; i++)
                if (_npServiceCombos[i].SelectedItem is string svc)
                    ThemeManager.NpSearchServiceSlots[i] = svc;
            UpdateNpCustomPanelVisibility();
            PersistNpSearchServices();
        }

        private void NpServiceVisible_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            for (int i = 0; i < 6; i++)
                ThemeManager.NpSearchServiceSlotVisible[i] = _npServiceVisibleChecks[i].IsChecked == true;
            PersistNpSearchServices();
        }

        private void NpCustomUrl_Changed(object sender, TextChangedEventArgs e)
        {
            if (_initializing) return;
            if (sender is TextBox tb && tb.Tag is int idx && idx >= 0 && idx < 6)
            {
                ThemeManager.NpSearchCustomServiceUrls[idx] = tb.Text;
                PersistNpSearchServices();
            }
        }

        private void NpBrowseIcon_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not int idx || idx < 0 || idx >= 6) return;

            var dialog = new OpenFileDialog
            {
                Title = "Select Icon Image",
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.ico;*.bmp|All Files|*.*"
            };
            if (dialog.ShowDialog() == true)
            {
                _npCustomIconBoxes[idx].Text = dialog.FileName;
                ThemeManager.NpSearchCustomServiceIcons[idx] = dialog.FileName;
                PersistNpSearchServices();
            }
        }

        private void NpSearchCopyFromMain_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.SeedNpSearchServicesFromMain(force: true);
            // Refresh the visible controls from the freshly copied values.
            bool wasInit = _initializing;
            _initializing = true;
            for (int i = 0; i < 6; i++)
            {
                _npServiceCombos[i].SelectedItem = ThemeManager.NpSearchServiceSlots[i];
                _npServiceVisibleChecks[i].IsChecked = ThemeManager.NpSearchServiceSlotVisible[i];
                _npCustomUrlBoxes[i].Text = ThemeManager.NpSearchCustomServiceUrls[i];
                _npCustomIconBoxes[i].Text = ThemeManager.NpSearchCustomServiceIcons[i];
            }
            UpdateNpCustomPanelVisibility();
            _initializing = wasInit;
            PersistNpSearchServices();
        }

        // Mark the NP slots as user-configured so they're no longer auto-seeded, save,
        // and drop the NP search logo cache so edits show immediately next popup open.
        private void PersistNpSearchServices()
        {
            ThemeManager.NpSearchServicesConfigured = true;
            ThemeManager.SavePlayOptions();
            if (Owner is MainWindow mw)
                mw.InvalidateNpSearchLogoCache();
        }
    }
}
