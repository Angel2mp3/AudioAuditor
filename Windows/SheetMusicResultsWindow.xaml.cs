using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using AudioQualityChecker.Models;
using AudioQualityChecker.Services;

namespace AudioQualityChecker
{
    /// <summary>
    /// Shows IMSLP sheet-music matches for one or more selected tracks, one track at a time
    /// with Prev/Next stepping, plus a "search the web" fallback for anything IMSLP doesn't cover
    /// (i.e. everything outside its public-domain/classical catalog).
    /// </summary>
    public partial class SheetMusicResultsWindow : Window
    {
        private readonly System.Collections.Generic.IReadOnlyList<AudioFileInfo> _files;
        private int _index;
        private int _requestId;   // generation stamp; see StartLoadCurrent

        public SheetMusicResultsWindow(System.Collections.Generic.IReadOnlyList<AudioFileInfo> files)
        {
            InitializeComponent();
            _files = files;
            if (_files.Count == 0)
            {
                // Both callers check for an empty selection, but an empty list must never
                // index-fault the constructor of a window the user can already see.
                TrackLabel.Text = "";
                StatusText.Text = "No tracks to look up.";
                PrevBtn.IsEnabled = NextBtn.IsEnabled = false;
                return;
            }

            PrevBtn.IsEnabled = false;
            NextBtn.IsEnabled = _files.Count > 1;
            StartLoadCurrent();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            this.SafeDragMove();
        }

        private AudioFileInfo CurrentFile => _files[_index];

        /// <summary>
        /// Kicks off a lookup for the current track, superseding any in flight. Stepping through
        /// tracks faster than IMSLP responds used to let an older, slower reply land on top of a
        /// newer track's results, so each request carries a generation stamp.
        /// </summary>
        private void StartLoadCurrent()
        {
            int requestId = ++_requestId;
            LoadCurrentAsync(requestId).Observe("Sheet music lookup", ex =>
            {
                if (requestId != _requestId) return;
                StatusText.Text = $"Could not reach IMSLP ({ex.GetType().Name}). Try the web search below.";
            });
        }

        private async System.Threading.Tasks.Task LoadCurrentAsync(int requestId)
        {
            var file = CurrentFile;
            TrackLabel.Text = !string.IsNullOrWhiteSpace(file.Artist) && !string.IsNullOrWhiteSpace(file.Title)
                ? $"{file.Artist} — {file.Title}"
                : System.IO.Path.GetFileNameWithoutExtension(file.FileName);

            ResultsList.Items.Clear();
            StatusText.Text = "Searching IMSLP...";

            var results = await SheetMusicLookupService.SearchImslpAsync(file.Artist ?? "", file.Title ?? "");
            if (requestId != _requestId) return;   // superseded by a newer Prev/Next step

            foreach (var r in results)
                ResultsList.Items.Add(r);

            StatusText.Text = results.Count > 0
                ? $"Found {results.Count} IMSLP match(es). Double-click to open."
                : "No IMSLP matches (IMSLP only covers public-domain/classical works). Try the web search below.";
        }

        private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ResultsList.SelectedItem is not SheetMusicResult result) return;
            OpenUrl(result.PageUrl);
        }

        private void PrevBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_index <= 0) return;
            _index--;
            NextBtn.IsEnabled = true;
            PrevBtn.IsEnabled = _index > 0;
            StartLoadCurrent();
        }

        private void NextBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_index >= _files.Count - 1) return;
            _index++;
            PrevBtn.IsEnabled = true;
            NextBtn.IsEnabled = _index < _files.Count - 1;
            StartLoadCurrent();
        }

        private void SearchWeb_Click(object sender, RoutedEventArgs e)
        {
            if (_files.Count == 0) return;
            var file = CurrentFile;
            string query = !string.IsNullOrWhiteSpace(file.Artist) && !string.IsNullOrWhiteSpace(file.Title)
                ? $"{file.Artist} {file.Title} sheet music"
                : $"{System.IO.Path.GetFileNameWithoutExtension(file.FileName)} sheet music";
            OpenUrl("https://www.google.com/search?q=" + Uri.EscapeDataString(query));
        }

        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ErrorDialog.Show("Browser Error", $"Could not open browser:\n{ex.Message}", this);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
