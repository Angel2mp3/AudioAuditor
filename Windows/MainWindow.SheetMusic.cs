using System.Linq;
using System.Windows;
using AudioQualityChecker.Models;

namespace AudioQualityChecker
{
    public partial class MainWindow
    {
        private void FindSheetMusic_Click(object sender, RoutedEventArgs e)
        {
            var selected = FileGrid.SelectedItems.Cast<AudioFileInfo>().ToList();
            if (selected.Count == 0)
            {
                ErrorDialog.Show("No Selection", "Select one or more songs first.", this);
                return;
            }

            new SheetMusicResultsWindow(selected) { Owner = this }.Show();
        }

        private void NpFindSheetMusic_Click(object sender, RoutedEventArgs e)
        {
            var current = NpGetCurrentTrackForSearch();
            if (current == null)
            {
                ErrorDialog.Show("Nothing playing", "Start playing a song to look it up.", this);
                return;
            }

            new SheetMusicResultsWindow(new[] { current }) { Owner = this }.Show();
        }
    }
}
