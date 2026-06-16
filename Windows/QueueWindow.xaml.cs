using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AudioQualityChecker.Models;

namespace AudioQualityChecker
{
    public partial class QueueWindow : Window
    {
        public ObservableCollection<AudioFileInfo> Queue { get; }
        public List<AudioFileInfo> UpNext { get; set; } = new();

        public QueueWindow(ObservableCollection<AudioFileInfo> queue)
        {
            InitializeComponent();
            Queue = queue;
            QueueList.ItemsSource = Queue;
            UpdateCount();
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            if (UpNext.Count > 0)
            {
                UpNextPanel.Visibility = Visibility.Visible;
                UpNextItems.Children.Clear();
                foreach (var track in UpNext.Take(3))
                {
                    string title = !string.IsNullOrWhiteSpace(track.Title) ? track.Title : track.FileName ?? "Unknown";
                    string artist = !string.IsNullOrWhiteSpace(track.Artist) ? track.Artist : "";
                    var tb = new TextBlock
                    {
                        Text = string.IsNullOrEmpty(artist) ? title : $"{title} — {artist}",
                        FontSize = 11,
                        FontFamily = new FontFamily("Segoe UI"),
                        Foreground = (Brush)FindResource("TextSecondary"),
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Margin = new Thickness(0, 1, 0, 1)
                    };
                    UpNextItems.Children.Add(tb);
                }
            }
        }

        public void ApplyColorMatch(Color primary, Color secondary, Color tertiary, Color background)
        {
            SetBrush("WindowBg", Color.FromArgb(245, background.R, background.G, background.B));
            SetBrush("GlassFloatingBg", Color.FromArgb(235, background.R, background.G, background.B));
            SetBrush("GlassBorderBrush", Color.FromArgb(120, primary.R, primary.G, primary.B));
            SetBrush("GridBg", Color.FromArgb(120, background.R, background.G, background.B));
            SetBrush("BorderColor", Color.FromArgb(90, primary.R, primary.G, primary.B));
            SetBrush("ButtonBg", Color.FromArgb(46, primary.R, primary.G, primary.B));
            SetBrush("ButtonHover", Color.FromArgb(72, primary.R, primary.G, primary.B));
            SetBrush("ButtonPressed", Color.FromArgb(92, primary.R, primary.G, primary.B));
            SetBrush("ButtonBorder", Color.FromArgb(100, primary.R, primary.G, primary.B));
            SetBrush("SelectionBg", Color.FromArgb(78, primary.R, primary.G, primary.B));
            SetBrush("RowHoverBg", Color.FromArgb(46, secondary.R, secondary.G, secondary.B));
            SetBrush("AccentColor", primary);
            SetBrush("PlaybarAccentColor", primary);
            SetBrush("TextPrimary", Colors.White);
            SetBrush("TextSecondary", Color.FromRgb(218, 224, 232));
            SetBrush("TextMuted", Color.FromRgb(166, 176, 190));
        }

        public void ClearColorMatch()
        {
            foreach (var key in new[]
            {
                "WindowBg", "GlassFloatingBg", "GlassBorderBrush", "GridBg", "BorderColor",
                "ButtonBg", "ButtonHover", "ButtonPressed", "ButtonBorder", "SelectionBg",
                "RowHoverBg", "AccentColor", "PlaybarAccentColor", "TextPrimary", "TextSecondary", "TextMuted"
            })
            {
                Resources.Remove(key);
            }
        }

        private void SetBrush(string key, Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            Resources[key] = brush;
        }

        private void UpdateCount()
        {
            QueueCount.Text = $"  ({Queue.Count} song{(Queue.Count == 1 ? "" : "s")})";
        }

        private void Header_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void MoveUp_Click(object sender, RoutedEventArgs e)
        {
            int idx = QueueList.SelectedIndex;
            if (idx > 0)
            {
                Queue.Move(idx, idx - 1);
                QueueList.SelectedIndex = idx - 1;
            }
        }

        private void MoveDown_Click(object sender, RoutedEventArgs e)
        {
            int idx = QueueList.SelectedIndex;
            if (idx >= 0 && idx < Queue.Count - 1)
            {
                Queue.Move(idx, idx + 1);
                QueueList.SelectedIndex = idx + 1;
            }
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            int idx = QueueList.SelectedIndex;
            if (idx >= 0)
            {
                Queue.RemoveAt(idx);
                if (Queue.Count > 0)
                    QueueList.SelectedIndex = Math.Min(idx, Queue.Count - 1);
                UpdateCount();
            }
        }

        private void ClearQueue_Click(object sender, RoutedEventArgs e)
        {
            Queue.Clear();
            UpdateCount();
        }

        private void QueueList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // Nothing special needed
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
