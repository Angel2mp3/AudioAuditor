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
