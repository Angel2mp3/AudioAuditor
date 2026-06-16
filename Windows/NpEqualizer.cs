using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using IOPath = System.IO.Path;
using AudioQualityChecker.Models;
using AudioQualityChecker.Services;
using AudioQualityChecker.Services.Scrobbling;

namespace AudioQualityChecker
{
    // Now Playing equalizer: band sliders, slider template, EQ panel show/hide/
    // theme. Extracted verbatim from NpCore.cs (2026-06-05 large-file split).
    // (EQ profile combo handlers remain in NpCore.cs.)
    public partial class MainWindow
    {
        // ═══════════════════════════════════════════
        //  Equalizer
        // ═══════════════════════════════════════════

        private static readonly string[] EqBandLabels =
            { "32", "64", "125", "250", "500", "1K", "2K", "4K", "8K", "16K" };

        private void InitializeEqualizerSliders()
        {
            EqSlidersPanel.Children.Clear();
            _eqSliders = new Slider[10];
            _eqValueLabels = new TextBlock[10];

            // Populate the profile dropdown the first time the EQ panel is built
            InitializeEqProfileCombo();

            for (int i = 0; i < 10; i++)
            {
                var bandPanel = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Width = 34,
                    // Wider gaps now that the controls row moved off to the side and freed horizontal
                    // space — spreads the 10 bands out so there's less empty area in the popup.
                    Margin = new Thickness(5, 0, 5, 0),
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                var valueLabel = new TextBlock
                {
                    Text = "0",
                    FontSize = 9,
                    FontFamily = new FontFamily("Segoe UI"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = (Brush)FindResource("TextMuted")
                };
                _eqValueLabels[i] = valueLabel;

                var slider = new Slider
                {
                    Minimum = -12,
                    Maximum = 12,
                    Value = ThemeManager.EqualizerGains[i],
                    Orientation = Orientation.Vertical,
                    Height = 65,
                    IsSnapToTickEnabled = true,
                    TickFrequency = 1,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Tag = i,
                    Width = 18
                };

                // Apply full themed vertical slider template via XAML
                slider.Template = GetEqSliderTemplate();

                slider.ValueChanged += EqSlider_ValueChanged;
                _eqSliders[i] = slider;

                var freqLabel = new TextBlock
                {
                    Text = EqBandLabels[i],
                    FontSize = 9,
                    FontFamily = new FontFamily("Segoe UI"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = (Brush)FindResource("TextSecondary")
                };

                bandPanel.Children.Add(valueLabel);
                bandPanel.Children.Add(slider);
                bandPanel.Children.Add(freqLabel);
                EqSlidersPanel.Children.Add(bandPanel);
            }
        }

        private ControlTemplate? _eqSliderTemplateCache;

        private ControlTemplate GetEqSliderTemplate()
        {
            if (_eqSliderTemplateCache != null) return _eqSliderTemplateCache;

            string accentColor;
            string trackColor;
            string strokeColor;

            if (NpGetEffectiveColorMatchPalette(out var cmPrimary, out var cmSecondary, out _, out var cmBackground, out _))
            {
                // ColorMatch ON: album-or-neutral colors only — never the theme accent.
                accentColor = EnsureMinLuminance(cmPrimary, 150).ToString();
                trackColor = System.Windows.Media.Color.FromArgb(90, cmBackground.R, cmBackground.G, cmBackground.B).ToString();
                strokeColor = EnsureMinLuminance(cmSecondary, 160).ToString();
            }
            else
            {
                // ColorMatch OFF: follow the active theme as before.
                var accentBrush = FindResource("AccentColor") as Brush ?? Brushes.DodgerBlue;
                var trackBrush = FindResource("ScrollBg") as Brush ?? Brushes.Gray;
                var thumbStroke = FindResource("TextPrimary") as Brush ?? Brushes.White;

                accentColor = "#3399FF";
                trackColor = "#333333";
                strokeColor = "#FFFFFF";

                if (accentBrush is SolidColorBrush ab) accentColor = ab.Color.ToString();
                if (trackBrush is SolidColorBrush tb) trackColor = tb.Color.ToString();
                if (thumbStroke is SolidColorBrush sb) strokeColor = sb.Color.ToString();
            }

            string xaml = $@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
                 TargetType='Slider'>
    <Grid>
        <!-- Track background -->
        <Border Width='4' CornerRadius='2' Background='{trackColor}'
                HorizontalAlignment='Center'/>
        <Track x:Name='PART_Track' IsDirectionReversed='true' Orientation='Vertical'>
            <Track.DecreaseRepeatButton>
                <RepeatButton IsTabStop='False' Focusable='False'>
                    <RepeatButton.Template>
                        <ControlTemplate TargetType='RepeatButton'>
                            <Border Width='4' CornerRadius='2' Background='{accentColor}'
                                    HorizontalAlignment='Center'/>
                        </ControlTemplate>
                    </RepeatButton.Template>
                </RepeatButton>
            </Track.DecreaseRepeatButton>
            <Track.IncreaseRepeatButton>
                <RepeatButton IsTabStop='False' Focusable='False'>
                    <RepeatButton.Template>
                        <ControlTemplate TargetType='RepeatButton'>
                            <Border Background='Transparent'/>
                        </ControlTemplate>
                    </RepeatButton.Template>
                </RepeatButton>
            </Track.IncreaseRepeatButton>
            <Track.Thumb>
                <Thumb OverridesDefaultStyle='True'>
                    <Thumb.Template>
                        <ControlTemplate TargetType='Thumb'>
                            <Ellipse Width='14' Height='14'
                                     Fill='{accentColor}' Stroke='{strokeColor}'
                                     StrokeThickness='1.2'/>
                        </ControlTemplate>
                    </Thumb.Template>
                </Thumb>
            </Track.Thumb>
        </Track>
    </Grid>
</ControlTemplate>";

            _eqSliderTemplateCache = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
            return _eqSliderTemplateCache;
        }

        private void EqSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (sender is not Slider slider || slider.Tag is not int idx) return;

            float gain = (float)slider.Value;
            _eqValueLabels[idx].Text = gain >= 0 ? $"+{(int)gain}" : $"{(int)gain}";

            ThemeManager.EqualizerGains[idx] = gain;

            var eq = _player.CurrentEqualizer;
            if (eq != null)
                eq.UpdateBand(idx, gain);

            ThemeManager.SavePlayOptions();
        }

        private void EqToggle_Click(object sender, RoutedEventArgs e)
        {
            SetEqualizerPanelVisible(EqPanel.Visibility != Visibility.Visible);
        }

        private void NpEq_Click(object sender, RoutedEventArgs e)
        {
            if (NpEqPopup?.IsOpen == true)
                NpEqPopup.IsOpen = false;
            else
                SetEqualizerPanelVisible(true);
        }

        private void SetEqualizerPanelVisible(bool visible)
        {
            if (_npVisible && visible)
            {
                ShowNowPlayingEqualizerPanel();
                return;
            }

            ReturnEqualizerPanelHome(collapse: !visible);

            if (visible)
            {
                InitializeEqualizerSliders();
                EqPanel.Visibility = Visibility.Visible;
                EqPanel.BringIntoView();
            }
            else
            {
                EqPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void ShowNowPlayingEqualizerPanel()
        {
            _eqSliderTemplateCache = null;
            InitializeEqualizerSliders();

            if (NpEqPopup.Child != EqPanel)
            {
                if (EqPanel.Parent is Panel currentParent)
                {
                    _eqPanelHome ??= currentParent;
                    _eqPanelHomeIndex = currentParent.Children.IndexOf(EqPanel);
                    currentParent.Children.Remove(EqPanel);
                }

                NpEqPopup.Child = EqPanel;
                NpEqPopup.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
                {
                    double x = (targetSize.Width - popupSize.Width) / 2.0;
                    double y = -popupSize.Height - 6;
                    return [new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.None)];
                };
            }

            EqPanel.Visibility = Visibility.Visible;
            ApplyEqualizerPanelTheme();
            NpEqPopup.IsOpen = true;
        }

        private void ApplyEqualizerPanelTheme()
        {
            if (_npVisible && _npColorMatchEnabled && (_npAlbumPrimary != default || NpHasColorPickerOverridesForCurrentTrack()))
            {
                var panel = System.Windows.Media.Color.FromArgb(238,
                    (byte)Math.Max(14, _npAlbumBackground.R / 3),
                    (byte)Math.Max(14, _npAlbumBackground.G / 3),
                    (byte)Math.Max(14, _npAlbumBackground.B / 3));
                var accent = EnsureMinLuminance(_npAlbumPrimary, 150);
                var secondary = EnsureMinLuminance(_npAlbumSecondary, 125);
                EqPanel.Background = new SolidColorBrush(panel);
                EqPanel.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(150, accent.R, accent.G, accent.B));
                EqProfileCombo.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(210, panel.R, panel.G, panel.B));
                EqProfileCombo.Foreground = new SolidColorBrush(EnsureMinLuminance(accent, 175));
                EqProfileCombo.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(130, secondary.R, secondary.G, secondary.B));
                BtnEqSaveProfile.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(70, accent.R, accent.G, accent.B));
                BtnEqSaveProfile.Foreground = new SolidColorBrush(EnsureMinLuminance(accent, 185));
                BtnEqSaveProfile.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(145, accent.R, accent.G, accent.B));
                BtnEqDeleteProfile.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(48, secondary.R, secondary.G, secondary.B));
                BtnEqDeleteProfile.Foreground = new SolidColorBrush(EnsureMinLuminance(secondary, 170));
                BtnEqDeleteProfile.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(130, secondary.R, secondary.G, secondary.B));
                return;
            }

            EqPanel.SetResourceReference(Border.BackgroundProperty, "PanelBg");
            EqPanel.SetResourceReference(Border.BorderBrushProperty, "ButtonBorder");
            EqProfileCombo.SetResourceReference(Control.BackgroundProperty, "InputBg");
            EqProfileCombo.SetResourceReference(Control.ForegroundProperty, "TextPrimary");
            EqProfileCombo.SetResourceReference(Control.BorderBrushProperty, "ButtonBorder");
            BtnEqSaveProfile.SetResourceReference(Control.BackgroundProperty, "ButtonBg");
            BtnEqSaveProfile.SetResourceReference(Control.ForegroundProperty, "TextPrimary");
            BtnEqSaveProfile.SetResourceReference(Control.BorderBrushProperty, "ButtonBorder");
            BtnEqDeleteProfile.SetResourceReference(Control.BackgroundProperty, "ButtonBg");
            BtnEqDeleteProfile.SetResourceReference(Control.ForegroundProperty, "TextPrimary");
            BtnEqDeleteProfile.SetResourceReference(Control.BorderBrushProperty, "ButtonBorder");
        }

        private void NpEqPopup_Closed(object? sender, EventArgs e)
        {
            ReturnEqualizerPanelHome(collapse: true);
        }

        private void ReturnEqualizerPanelHome(bool collapse)
        {
            if (NpEqPopup?.Child == EqPanel)
                NpEqPopup.Child = null;

            if (_eqPanelHome != null && EqPanel.Parent == null)
            {
                int index = _eqPanelHomeIndex >= 0 && _eqPanelHomeIndex <= _eqPanelHome.Children.Count
                    ? _eqPanelHomeIndex
                    : _eqPanelHome.Children.Count;
                _eqPanelHome.Children.Insert(index, EqPanel);
            }

            if (collapse)
                EqPanel.Visibility = Visibility.Collapsed;
        }
    }
}
