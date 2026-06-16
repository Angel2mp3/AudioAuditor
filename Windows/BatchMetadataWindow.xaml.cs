using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using AudioQualityChecker.Models;
using AudioQualityChecker.Services;

namespace AudioQualityChecker;

public partial class BatchMetadataWindow : Window
{
    private readonly List<AudioFileInfo> _files;
    private readonly ObservableCollection<MetadataEnrichmentChange> _changes = new();
    private readonly MetadataEnrichmentService _service = new();
    private CancellationTokenSource? _searchCts;

    public bool MetadataChanged { get; private set; }

    public BatchMetadataWindow(IEnumerable<AudioFileInfo> files, Window owner)
    {
        InitializeComponent();
        Owner = owner;
        _files = files.ToList();
        FileCountText.Text = $"{_files.Count} file{(_files.Count == 1 ? "" : "s")} selected";
        ChangesGrid.ItemsSource = _changes;
        StatusText.Text = "Choose providers and fields, then search.";
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _searchCts?.Cancel();
        Close();
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        _changes.Clear();
        SetBusy(true, "Searching metadata providers...");

        try
        {
            var options = BuildOptions();
            var progress = new Progress<(int done, int total, string fileName)>(p =>
            {
                StatusText.Text = p.done >= p.total
                    ? "Search complete."
                    : $"Searching {p.done + 1}/{p.total}: {p.fileName}";
            });

            var previews = await _service.PreviewAsync(_files, options, progress, _searchCts.Token);
            foreach (var change in previews.SelectMany(p => p.Changes))
                _changes.Add(change);

            int selected = _changes.Count(c => c.IsSelected);
            StatusText.Text = _changes.Count == 0
                ? "No safe changes found. Try disabling Missing only or enabling another provider."
                : $"Found {_changes.Count} proposed change{(_changes.Count == 1 ? "" : "s")}; {selected} high-confidence selected.";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Search cancelled.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Search failed: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SelectHigh_Click(object sender, RoutedEventArgs e)
    {
        foreach (var change in _changes)
            change.IsSelected = change.Confidence >= MetadataEnrichmentService.HighConfidenceThreshold;

        ChangesGrid.Items.Refresh();
        StatusText.Text = $"Selected {_changes.Count(c => c.IsSelected)} high-confidence change{(_changes.Count(c => c.IsSelected) == 1 ? "" : "s")}.";
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        var selected = _changes.Where(c => c.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusText.Text = "No changes selected.";
            return;
        }

        var confirm = MessageBox.Show(
            $"Apply {selected.Count} selected metadata change{(selected.Count == 1 ? "" : "s")}?\n\nBackups are recommended before writing tags.",
            "Apply Metadata Changes",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        SetBusy(true, "Writing selected metadata changes...");
        try
        {
            var summary = await _service.ApplyAsync(selected, ChkBackups.IsChecked == true);
            ApplyToInMemoryRows(selected);
            MetadataChanged = summary.FilesChanged > 0;
            StatusText.Text = $"Applied {summary.ChangesApplied} change{(summary.ChangesApplied == 1 ? "" : "s")} to {summary.FilesChanged} file{(summary.FilesChanged == 1 ? "" : "s")}.";
            if (summary.FailedFiles > 0)
                StatusText.Text += $" {summary.FailedFiles} failed.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Write failed: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private MetadataEnrichmentOptions BuildOptions()
    {
        var options = MetadataEnrichmentOptions.CreateDefault();
        options.MissingOnly = ChkMissingOnly.IsChecked == true;
        options.ReplaceExistingCover = ChkReplaceCovers.IsChecked == true;
        options.UseMusicBrainz = ChkMusicBrainz.IsChecked == true;
        options.UseCoverArtArchive = ChkCoverArchive.IsChecked == true;
        options.UseITunes = ChkITunes.IsChecked == true;
        options.UseAcoustId = ChkAcoustId.IsChecked == true;
        options.AcoustIdApiKey = ThemeManager.AcoustIdApiKey;
        options.EnabledFields = new HashSet<MetadataEnrichmentField>();

        AddField(options, FldTitle.IsChecked == true, MetadataEnrichmentField.Title);
        AddField(options, FldArtist.IsChecked == true, MetadataEnrichmentField.Artist);
        AddField(options, FldAlbum.IsChecked == true, MetadataEnrichmentField.Album);
        AddField(options, FldAlbumArtist.IsChecked == true, MetadataEnrichmentField.AlbumArtist);
        AddField(options, FldYear.IsChecked == true, MetadataEnrichmentField.Year);
        AddField(options, FldTrack.IsChecked == true, MetadataEnrichmentField.TrackNumber);
        AddField(options, FldDisc.IsChecked == true, MetadataEnrichmentField.DiscNumber);
        AddField(options, FldGenre.IsChecked == true, MetadataEnrichmentField.Genre);
        AddField(options, FldComposer.IsChecked == true, MetadataEnrichmentField.Composer);
        AddField(options, FldComment.IsChecked == true, MetadataEnrichmentField.Comment);
        AddField(options, FldLyrics.IsChecked == true, MetadataEnrichmentField.Lyrics);
        AddField(options, FldCopyright.IsChecked == true, MetadataEnrichmentField.Copyright);
        AddField(options, FldCover.IsChecked == true, MetadataEnrichmentField.CoverArt);
        return options;
    }

    private static void AddField(MetadataEnrichmentOptions options, bool enabled, MetadataEnrichmentField field)
    {
        if (enabled) options.EnabledFields.Add(field);
    }

    private void ApplyToInMemoryRows(IEnumerable<MetadataEnrichmentChange> selected)
    {
        var byPath = _files.ToDictionary(f => f.FilePath, StringComparer.OrdinalIgnoreCase);
        foreach (var change in selected)
        {
            if (!byPath.TryGetValue(change.FilePath, out var file)) continue;
            switch (change.Field)
            {
                case MetadataEnrichmentField.Title:
                    file.Title = change.NewValue;
                    break;
                case MetadataEnrichmentField.Artist:
                    file.Artist = change.NewValue;
                    break;
                case MetadataEnrichmentField.Album:
                    file.Album = change.NewValue;
                    break;
                case MetadataEnrichmentField.CoverArt:
                    file.HasAlbumCover = true;
                    break;
            }
        }
    }

    private void SetBusy(bool busy, string? status = null)
    {
        if (status != null) StatusText.Text = status;
        Cursor = busy ? Cursors.Wait : Cursors.Arrow;
    }
}
