using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AudioQualityChecker.Models;
using AudioQualityChecker.Services;

namespace AudioQualityChecker;

public partial class NowPlayingWindow : Window
{
    private readonly AudioPlayer _player;
    private readonly Action _onPrev;
    private readonly Action _onNext;
    private readonly Action _onShuffleToggle;
    private readonly Func<bool> _getShuffleState;

    private DispatcherTimer? _updateTimer;
    private LyricsResult _currentLyrics = LyricsResult.Empty;
    private int _currentLyricIndex = -1;
    private readonly List<TextBlock> _lyricTextBlocks = new();
    private LyricService.LyricProvider _lyricProvider = LyricService.LyricProvider.Auto;
    private AudioFileInfo? _currentFile;
    private bool _isSeeking;
    private int _lyricsVersion;

    private static readonly (LyricService.LyricProvider Provider, string Name)[] LyricProviders =
    {
        (LyricService.LyricProvider.Auto, "Auto"),
        (LyricService.LyricProvider.LrcFile, "LRC File"),
        (LyricService.LyricProvider.Embedded, "Embedded"),
        (LyricService.LyricProvider.LrcLib, "LRCLIB"),
        (LyricService.LyricProvider.Netease, "Netease"),
        (LyricService.LyricProvider.Musixmatch, "Musixmatch"),
    };
    private int _providerIndex;

    public NowPlayingWindow(
        AudioPlayer player,
        Action onPrev,
        Action onNext,
        Action onShuffleToggle,
        Func<bool> getShuffleState)
    {
        InitializeComponent();
        _player = player;
        _onPrev = onPrev;
        _onNext = onNext;
        _onShuffleToggle = onShuffleToggle;
        _getShuffleState = getShuffleState;

        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _updateTimer.Tick += UpdateTimer_Tick;
        _updateTimer.Start();

        Closed += (_, _) =>
        {
            _updateTimer?.Stop();
            _updateTimer = null;
        };

        UpdateShuffleIcon();
    }

    public void SetTrack(AudioFileInfo file, double volume)
    {
        _currentFile = file;
        NpSongTitle.Text = file.Title ?? file.FileName ?? "Unknown";
        NpSongArtist.Text = file.Artist ?? "";
        NpVolumeSlider.Value = volume;

        if (file.FilePath != null)
        {
            LoadCover(file.FilePath);
            LoadLyrics(file.FilePath);
        }

        UpdatePlayState();
    }

    public void UpdateVolume(double volume) => NpVolumeSlider.Value = volume;

    // ─── Cover Art ───

    private void LoadCover(string filePath)
    {
        try
        {
            var tagFile = TagLib.File.Create(filePath);
            if (tagFile.Tag.Pictures.Length > 0)
            {
                var pic = tagFile.Tag.Pictures[0];
                var imageData = pic.Data.Data;
                using var ms = new MemoryStream(imageData);

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();

                NpCoverImage.Source = bmp;

                double maxDim = 420;
                double scale = Math.Min(maxDim / bmp.PixelWidth, maxDim / bmp.PixelHeight);
                NpCoverImage.Width = bmp.PixelWidth * scale;
                NpCoverImage.Height = bmp.PixelHeight * scale;

                ApplyGlow(bmp);
            }
            else
            {
                ClearCover();
            }
        }
        catch
        {
            ClearCover();
        }
    }

    private void ClearCover()
    {
        NpCoverImage.Source = null;
        NpCoverGlow1.Background = Brushes.Transparent;
        NpCoverGlow2.Background = Brushes.Transparent;
        BgGradient.Background = new SolidColorBrush(Color.FromRgb(15, 15, 25));
    }

    private void ApplyGlow(BitmapSource bmp)
    {
        try
        {
            // Convert to BGRA32 for AlbumColorExtractor
            var converted = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
            int w = converted.PixelWidth;
            int h = converted.PixelHeight;
            int stride = w * 4;
            var pixels = new byte[stride * h];
            converted.CopyPixels(pixels, stride, 0);

            var colors = AlbumColorExtractor.Extract(pixels, w, h, stride);

            NpCoverGlow1.Background = new SolidColorBrush(
                Color.FromArgb(180, colors.Primary.R, colors.Primary.G, colors.Primary.B));
            NpCoverGlow2.Background = new SolidColorBrush(
                Color.FromArgb(120, colors.Secondary.R, colors.Secondary.G, colors.Secondary.B));

            var bg1 = Color.FromArgb(200, colors.Background.R, colors.Background.G, colors.Background.B);
            var bg2 = Color.FromRgb(10, 10, 18);
            BgGradient.Background = new LinearGradientBrush(bg1, bg2, 45);
        }
        catch
        {
            BgGradient.Background = new SolidColorBrush(Color.FromRgb(15, 15, 25));
        }
    }

    // ─── Lyrics ───

    private async void LoadLyrics(string filePath)
    {
        int version = ++_lyricsVersion;
        string? artist = _currentFile?.Artist;
        string? title = _currentFile?.Title;
        double duration = _currentFile?.DurationSeconds ?? 0;

        _currentLyrics = await LyricService.GetLyricsAsync(
            filePath, _lyricProvider, artist, title, durationSeconds: duration);

        if (version != _lyricsVersion) return; // track changed during fetch

        _currentLyricIndex = -1;
        BuildLyricLines();
    }

    private void BuildLyricLines()
    {
        LyricsPanel.Children.Clear();
        _lyricTextBlocks.Clear();

        if (!_currentLyrics.HasLyrics)
        {
            var noLyrics = new TextBlock
            {
                Text = "No lyrics available for this track",
                Foreground = new SolidColorBrush(Color.FromArgb(68, 255, 255, 255)),
                FontSize = 16,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 60, 0, 0)
            };
            LyricsPanel.Children.Add(noLyrics);
            NpLyricsSource.Text = "";
            return;
        }

        NpLyricsSource.Text = _currentLyrics.IsTimed
            ? $"Source: {_currentLyrics.Source} (synced)"
            : $"Source: {_currentLyrics.Source} (static)";

        LyricsPanel.Children.Add(new Border { Height = 80 });

        foreach (var line in _currentLyrics.Lines)
        {
            var tb = new TextBlock
            {
                Text = line.Text,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Left,
                Margin = new Thickness(0, 6, 0, 6),
                FontSize = 18,
                Foreground = new SolidColorBrush(Color.FromArgb(85, 255, 255, 255))
            };
            _lyricTextBlocks.Add(tb);
            LyricsPanel.Children.Add(tb);
        }

        LyricsPanel.Children.Add(new Border { Height = 200 });
    }

    private void UpdateLyricHighlight(TimeSpan position)
    {
        if (!_currentLyrics.IsTimed || _lyricTextBlocks.Count == 0) return;

        int newIdx = -1;
        for (int i = _currentLyrics.Lines.Count - 1; i >= 0; i--)
        {
            if (position >= _currentLyrics.Lines[i].Time)
            {
                newIdx = i;
                break;
            }
        }

        if (newIdx == _currentLyricIndex) return;
        _currentLyricIndex = newIdx;

        for (int i = 0; i < _lyricTextBlocks.Count; i++)
        {
            var tb = _lyricTextBlocks[i];
            if (i == newIdx)
            {
                AnimateForeground(tb, Colors.White);
                tb.FontSize = 22;
                tb.FontWeight = FontWeights.SemiBold;
            }
            else if (i < newIdx)
            {
                AnimateForeground(tb, Color.FromArgb(68, 255, 255, 255));
                tb.FontSize = 18;
                tb.FontWeight = FontWeights.Normal;
            }
            else
            {
                AnimateForeground(tb, Color.FromArgb(85, 255, 255, 255));
                tb.FontSize = 18;
                tb.FontWeight = FontWeights.Normal;
            }
        }

        // Auto-scroll
        if (newIdx >= 0 && newIdx < _lyricTextBlocks.Count)
        {
            var target = _lyricTextBlocks[newIdx];
            var transform = target.TransformToAncestor(LyricsPanel);
            var point = transform.Transform(new Point(0, 0));
            double scrollerHeight = LyricsScroller.ViewportHeight;
            double targetY = point.Y - scrollerHeight * 0.35;
            if (targetY < 0) targetY = 0;

            double current = LyricsScroller.VerticalOffset;
            double step = (targetY - current) * 0.3;
            LyricsScroller.ScrollToVerticalOffset(current + step);
        }
    }

    private static void AnimateForeground(TextBlock tb, Color target)
    {
        var anim = new ColorAnimation
        {
            To = target,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new QuadraticEase()
        };
        var brush = tb.Foreground as SolidColorBrush;
        if (brush == null || brush.IsFrozen)
        {
            brush = new SolidColorBrush(brush?.Color ?? Colors.Transparent);
            tb.Foreground = brush;
        }
        brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
    }

    // ─── Timer Update ───

    private void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        if (_player == null) return;

        var pos = _player.CurrentPosition;
        var total = _player.TotalDuration;

        NpTimeElapsed.Text = FormatTime(pos);
        NpTimeTotal.Text = FormatTime(total);

        if (!_isSeeking && total.TotalSeconds > 0)
        {
            NpSeekSlider.Maximum = total.TotalSeconds;
            NpSeekSlider.Value = pos.TotalSeconds;
        }

        UpdatePlayState();
        UpdateLyricHighlight(pos);
    }

    private void UpdatePlayState()
    {
        NpPlayIcon.Text = _player?.IsPlaying == true ? "\u23F8" : "\u25B6";
    }

    private static string FormatTime(TimeSpan ts) =>
        ts.TotalHours >= 1
            ? ts.ToString(@"h\:mm\:ss")
            : ts.ToString(@"m\:ss");

    // ─── Control Events ───

    private void NpPlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_player == null) return;
        if (_player.IsPlaying)
            _player.Pause();
        else
            _player.Resume();
        UpdatePlayState();
    }

    private void NpPrev_Click(object sender, RoutedEventArgs e) => _onPrev();
    private void NpNext_Click(object sender, RoutedEventArgs e) => _onNext();

    private void NpShuffle_Click(object sender, RoutedEventArgs e)
    {
        _onShuffleToggle();
        UpdateShuffleIcon();
    }

    private void UpdateShuffleIcon()
    {
        NpShuffleIcon.Foreground = _getShuffleState()
            ? Brushes.White
            : new SolidColorBrush(Color.FromArgb(128, 255, 255, 255));
    }

    private void NpLyricSource_Click(object sender, RoutedEventArgs e)
    {
        _providerIndex = (_providerIndex + 1) % LyricProviders.Length;
        _lyricProvider = LyricProviders[_providerIndex].Provider;
        NpLyricSourceText.Text = $"\U0001F3A4 {LyricProviders[_providerIndex].Name}";

        if (_currentFile?.FilePath != null)
            LoadLyrics(_currentFile.FilePath);
    }

    private void NpVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_player != null)
            _player.Volume = (float)(NpVolumeSlider.Value / 100.0);
    }

    private void NpSeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Only seek when user is dragging
        if (!_isSeeking || _player == null) return;
        var sliderVal = NpSeekSlider.Value;
        _player.Seek(sliderVal);
    }

    private void NpSeekSlider_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _isSeeking = true;
    }

    private void NpSeekSlider_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_isSeeking && _player != null)
        {
            _player.Seek(NpSeekSlider.Value);
        }
        _isSeeking = false;
    }

    private void NpClose_Click(object sender, RoutedEventArgs e) => Close();
}
