using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AudioQualityChecker.Models;
using AudioQualityChecker.Services;
using AudioQualityChecker.Services.Scrobbling;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace AudioQualityChecker
{
    // Custom theme editor: wiring, load/preview, create-from-editor, and the
    // save/delete/reset/import/export handlers. Extracted verbatim from
    // SettingsWindow.xaml.cs (2026-06-05 large-file split).
    public partial class SettingsWindow
    {
        private void WireCustomThemeEditorEvents()
        {
            void Text(TextBox box) => box.TextChanged += (_, _) => UpdateCustomThemePreview();
            Text(CustomThemeNameBox);
            Text(CustomAccentBox);
            Text(CustomWindowBox);
            Text(CustomPanelBox);
            Text(CustomTextBox);
            Text(CustomBorderBox);
            Text(CustomPlaybar1Box);
            Text(CustomPlaybar2Box);
            Text(CustomPlaybar3Box);
            Text(CustomViz1Box);
            Text(CustomViz2Box);
            Text(CustomViz3Box);

            void Slider(Slider slider) => slider.ValueChanged += (_, _) => UpdateCustomThemePreview();
            Slider(CustomCornerSlider);
        }

        private void LoadCustomThemeEditor(CustomThemeDefinition theme)
        {
            var wasInitializing = _initializing;
            _initializing = true;
            _customThemeEditorBase = theme.Sanitize();

            CustomThemeNameBox.Text = _customThemeEditorBase.Name;
            CustomAccentBox.Text = _customThemeEditorBase.AccentColor;
            CustomWindowBox.Text = _customThemeEditorBase.WindowBackground;
            CustomPanelBox.Text = _customThemeEditorBase.PanelBackground;
            CustomTextBox.Text = _customThemeEditorBase.TextPrimary;
            CustomBorderBox.Text = _customThemeEditorBase.BorderColor;
            CustomPlaybar1Box.Text = _customThemeEditorBase.PlaybarColors[0];
            CustomPlaybar2Box.Text = _customThemeEditorBase.PlaybarColors[1];
            CustomPlaybar3Box.Text = _customThemeEditorBase.PlaybarColors[2];
            CustomViz1Box.Text = _customThemeEditorBase.VisualizerColors[0];
            CustomViz2Box.Text = _customThemeEditorBase.VisualizerColors[1];
            CustomViz3Box.Text = _customThemeEditorBase.VisualizerColors[2];
            CustomCornerSlider.Value = _customThemeEditorBase.CornerSoftness;

            _initializing = wasInitializing;
            UpdateCustomThemePreview();
        }

        private CustomThemeDefinition CreateThemeFromEditor()
        {
            var baseTheme = ThemeCombo.SelectedItem as string ?? ThemeManager.CurrentTheme;
            return (_customThemeEditorBase with
            {
                Name = string.IsNullOrWhiteSpace(CustomThemeNameBox.Text) ? "Custom Theme" : CustomThemeNameBox.Text.Trim(),
                BaseTheme = baseTheme,
                WindowBackground = NormalizeHex(CustomWindowBox.Text, _customThemeEditorBase.WindowBackground),
                PanelBackground = NormalizeHex(CustomPanelBox.Text, _customThemeEditorBase.PanelBackground),
                ToolbarBackground = NormalizeHex(CustomPanelBox.Text, _customThemeEditorBase.ToolbarBackground),
                HeaderBackground = NormalizeHex(CustomPanelBox.Text, _customThemeEditorBase.HeaderBackground),
                GridBackground = NormalizeHex(CustomWindowBox.Text, _customThemeEditorBase.GridBackground),
                GridRowBackground = NormalizeHex(CustomWindowBox.Text, _customThemeEditorBase.GridRowBackground),
                GridAltRowBackground = NormalizeHex(CustomPanelBox.Text, _customThemeEditorBase.GridAltRowBackground),
                BorderColor = NormalizeHex(CustomBorderBox.Text, _customThemeEditorBase.BorderColor),
                InputBackground = NormalizeHex(CustomPanelBox.Text, _customThemeEditorBase.InputBackground),
                SelectionColor = NormalizeHex(CustomAccentBox.Text, _customThemeEditorBase.SelectionColor),
                ButtonBackground = NormalizeHex(CustomPanelBox.Text, _customThemeEditorBase.ButtonBackground),
                ButtonBorder = NormalizeHex(CustomBorderBox.Text, _customThemeEditorBase.ButtonBorder),
                ButtonHover = NormalizeHex(CustomAccentBox.Text, _customThemeEditorBase.ButtonHover),
                ButtonPressed = NormalizeHex(CustomAccentBox.Text, _customThemeEditorBase.ButtonPressed),
                AccentColor = NormalizeHex(CustomAccentBox.Text, _customThemeEditorBase.AccentColor),
                TextPrimary = NormalizeHex(CustomTextBox.Text, _customThemeEditorBase.TextPrimary),
                TextSecondary = NormalizeHex(CustomTextBox.Text, _customThemeEditorBase.TextSecondary),
                TextMuted = NormalizeHex(CustomBorderBox.Text, _customThemeEditorBase.TextMuted),
                TextDim = NormalizeHex(CustomBorderBox.Text, _customThemeEditorBase.TextDim),
                GlassTint = NormalizeHex(CustomPanelBox.Text, _customThemeEditorBase.GlassTint),
                GlassOpacity = 1.0,
                GlassBlur = 0,
                GlassHighlight = 0,
                GlassShadow = 0,
                CornerSoftness = CustomCornerSlider.Value,
                PlaybarColors =
                [
                    NormalizeHex(CustomPlaybar1Box.Text, _customThemeEditorBase.PlaybarColors[0]),
                    NormalizeHex(CustomPlaybar2Box.Text, _customThemeEditorBase.PlaybarColors[1]),
                    NormalizeHex(CustomPlaybar3Box.Text, _customThemeEditorBase.PlaybarColors[2])
                ],
                VisualizerColors =
                [
                    NormalizeHex(CustomViz1Box.Text, _customThemeEditorBase.VisualizerColors[0]),
                    NormalizeHex(CustomViz2Box.Text, _customThemeEditorBase.VisualizerColors[1]),
                    NormalizeHex(CustomViz3Box.Text, _customThemeEditorBase.VisualizerColors[2])
                ]
            }).Sanitize();
        }

        private void UpdateCustomThemePreview()
        {
            if (_initializing || CustomThemePreview == null) return;

            var theme = CreateThemeFromEditor();
            CustomThemePreview.Background = BrushFromHex(theme.PanelBackground);
            CustomThemePreview.BorderBrush = BrushFromHex(theme.BorderColor, 220);
            CustomThemePreview.CornerRadius = new CornerRadius(theme.CornerSoftness);
            if (CustomThemePreview.Child is StackPanel panel && panel.Children.Count > 2 && panel.Children[2] is Border bar)
                bar.Background = BrushFromHex(theme.PlaybarColors[1]);
        }

        private static SolidColorBrush BrushFromHex(string hex, byte alpha = 255)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex)!;
            color.A = alpha;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static string NormalizeHex(string value, string fallback)
        {
            var text = (value ?? "").Trim();
            if (!text.StartsWith("#", StringComparison.Ordinal))
                text = "#" + text;
            return text.Length == 7 && text.Skip(1).All(Uri.IsHexDigit)
                ? text.ToUpperInvariant()
                : fallback;
        }

        private void PickCustomThemeColor_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not TextBox box)
                return;

            using var dialog = new Forms.ColorDialog
            {
                FullOpen = true,
                Color = System.Drawing.ColorTranslator.FromHtml(NormalizeHex(box.Text, "#5865F2"))
            };
            if (dialog.ShowDialog() == Forms.DialogResult.OK)
                box.Text = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        }

        private void CustomThemeDuplicate_Click(object sender, RoutedEventArgs e)
        {
            var current = CreateThemeFromEditor();
            LoadCustomThemeEditor(current with { Name = $"{current.Name} Copy" });
        }

        private void CustomThemeSave_Click(object sender, RoutedEventArgs e)
        {
            var theme = CreateThemeFromEditor();
            CustomThemeStore.SaveTheme(theme);
            RefreshThemeCombos(theme.Name);
            ThemeManager.ApplyTheme(theme.Name);
            LoadCustomThemeEditor(theme);
        }

        private void CustomThemeDelete_Click(object sender, RoutedEventArgs e)
        {
            var selected = ThemeCombo.SelectedItem as string ?? CustomThemeNameBox.Text;
            if (ThemeManager.AvailableThemes.Contains(selected))
            {
                MessageBox.Show("Built-in themes cannot be deleted.", "Custom Themes", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show($"Delete custom theme \"{selected}\"?", "Custom Themes", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            CustomThemeStore.DeleteTheme(selected);
            RefreshThemeCombos("Blurple");
            ThemeManager.ApplyTheme("Blurple");
            LoadCustomThemeEditor(CustomThemeDefinition.CreateDefault());
        }

        private void CustomThemeReset_Click(object sender, RoutedEventArgs e)
        {
            var selected = ThemeCombo.SelectedItem as string ?? "Blurple";
            LoadCustomThemeEditor(ThemeManager.GetThemeDefinition(selected)
                ?? (CustomThemeDefinition.CreateDefault() with { BaseTheme = selected, Name = $"{selected} Custom" }));
        }

        private void CustomThemeImport_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "AudioAuditor Theme (*.json)|*.json|All files (*.*)|*.*",
                Title = "Import Custom Theme"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                var theme = CustomThemeStore.ImportTheme(dialog.FileName);
                RefreshThemeCombos(theme.Name);
                ThemeManager.ApplyTheme(theme.Name);
                LoadCustomThemeEditor(theme);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not import theme: {ex.Message}", "Custom Themes", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CustomThemeExport_Click(object sender, RoutedEventArgs e)
        {
            var theme = CreateThemeFromEditor();
            var dialog = new SaveFileDialog
            {
                Filter = "AudioAuditor Theme (*.json)|*.json|All files (*.*)|*.*",
                FileName = $"{theme.Name}.json",
                Title = "Export Custom Theme"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                CustomThemeStore.ExportTheme(theme, dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not export theme: {ex.Message}", "Custom Themes", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
