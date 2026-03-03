using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using AudioQualityChecker.Models;

namespace AudioQualityChecker
{
    public partial class QueueWindow : Window
    {
        public ObservableCollection<AudioFileInfo> Queue { get; }

        public QueueWindow(ObservableCollection<AudioFileInfo> queue)
        {
            InitializeComponent();
            Queue = queue;
            QueueList.ItemsSource = Queue;
            UpdateCount();
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
