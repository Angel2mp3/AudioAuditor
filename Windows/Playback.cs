using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using IOPath = System.IO.Path;
using AudioQualityChecker.Models;
using AudioQualityChecker.Services;
using AudioQualityChecker.Services.Scrobbling;

namespace AudioQualityChecker
{
    public partial class MainWindow
    {        // ═══════════════════════════════════════════
        //  Audio Player
        // ═══════════════════════════════════════════

        private void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_player.IsPlaying)
            {
                _player.Pause();
                _playerTimer.Stop();
                _pendingPlaybackVisual = false; // explicit pause overrides any in-flight skip visual

                // Fix: update cached playing state immediately to stop waveform progress advancing
                _isPlayingCached = false;

                UpdatePlayerUI();

                // Discord: show paused — use the actual playing file, not the grid selection
                var discordFile = _player.CurrentFile != null
                    ? _files.FirstOrDefault(f => string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase))
                    : null;
                _discord.UpdatePresence(discordFile?.Artist, discordFile?.Title, discordFile?.FileName,
                    _player.TotalDuration, _player.CurrentPosition, true);

                _smtc?.UpdatePlaybackState(false, true);
            }
            else if (_player.IsPaused)
            {
                _player.Resume();

                // Fix: restore cached playing state for smooth waveform interpolation
                _cachedPositionSec = _player.CurrentPosition.TotalSeconds;
                _cachedDurationSec = _player.TotalDuration.TotalSeconds;
                _cachedPositionTime = DateTime.UtcNow;
                _isPlayingCached = true;

                _playerTimer.Start();
                UpdatePlayerUI();

                // Discord: show playing again — use the actual playing file, not the grid selection
                var discordFile = _player.CurrentFile != null
                    ? _files.FirstOrDefault(f => string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase))
                    : null;
                _discord.UpdatePresence(discordFile?.Artist, discordFile?.Title, discordFile?.FileName,
                    _player.TotalDuration, _player.CurrentPosition, false);

                _smtc?.UpdatePlaybackState(true, false);
            }
            else if (FileGrid.SelectedItem is AudioFileInfo file2)
            {
                PlayFile(file2, isManualSkip: true);
            }
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            _crossfadeEarlyTriggered = false;
            _pendingPlaybackVisual = false; // explicit stop overrides any in-flight skip visual
            _player.Stop();
            _playerTimer.Stop();
            StopWaveformAnimation();
            WaveformCanvas.Children.Clear();
            UpdatePlayerUI();
            _discord.ClearPresence();
            _smtc?.UpdatePlaybackState(false, false);
            _scrobbler.OnTrackEnded();
        }

        private void Rewind_Click(object sender, RoutedEventArgs e)
        {
            if (_player.IsPlaying || _player.IsPaused)
            {
                _player.SeekRelative(-5);
                _lastSeekTime = DateTime.UtcNow;
                _lastSeekTargetPosition = _player.CurrentPosition;
                // Immediately update the UI slider to reflect new position
                if (_player.TotalDuration.TotalSeconds > 0)
                    SeekSlider.Value = _player.CurrentPosition.TotalSeconds;
                UpdatePlayerTimeText();
            }
        }

        private void Forward_Click(object sender, RoutedEventArgs e)
        {
            if (_player.IsPlaying || _player.IsPaused)
            {
                _player.SeekRelative(5);
                _lastSeekTime = DateTime.UtcNow;
                _lastSeekTargetPosition = _player.CurrentPosition;
                // Immediately update the UI slider to reflect new position
                if (_player.TotalDuration.TotalSeconds > 0)
                    SeekSlider.Value = _player.CurrentPosition.TotalSeconds;
                UpdatePlayerTimeText();
            }
        }

        private void Shuffle_Click(object sender, RoutedEventArgs e)
        {
            _shuffleMode = !_shuffleMode;
            if (_shuffleMode)
            {
                // Reset the deck so a fresh shuffle starts immediately
                _shuffleEngine.Reset();
            }
            UpdateShuffleUI();
        }

        private void Loop_Click(object sender, RoutedEventArgs e)
        {
            CycleLoopMode();
        }

        private void CycleLoopMode()
        {
            ThemeManager.LoopMode = ThemeManager.LoopMode switch
            {
                LoopMode.Off => LoopMode.All,
                LoopMode.All => LoopMode.One,
                _ => LoopMode.Off
            };
            ThemeManager.SavePlayOptions();
            UpdateLoopUI();
        }

        private void UpdateLoopUI()
        {
            var mode = ThemeManager.LoopMode;
            var accent = (System.Windows.Media.Brush)FindResource("PlaybarAccentColor");
            var muted = (System.Windows.Media.Brush)FindResource("TextMuted");
            bool active = mode != LoopMode.Off;

            if (LoopIcon != null)
            {
                LoopIcon.Stroke = active ? accent : muted;
                LoopIcon.StrokeThickness = active ? 2.0 : 1.6;
            }
            if (LoopOneIndicator != null)
            {
                LoopOneIndicator.Visibility = mode == LoopMode.One ? Visibility.Visible : Visibility.Collapsed;
                LoopOneIndicator.Foreground = accent;
            }
            if (BtnLoop != null)
            {
                string tip = mode switch { LoopMode.All => "Loop: All", LoopMode.One => "Loop: One", _ => "Loop: Off" };
                BtnLoop.ToolTip = tip;
                if (active && accent is System.Windows.Media.SolidColorBrush scb)
                {
                    var glowColor = scb.Color;
                    glowColor.A = 40;
                    BtnLoop.Background = new System.Windows.Media.SolidColorBrush(glowColor);
                }
                else
                {
                    BtnLoop.Background = System.Windows.Media.Brushes.Transparent;
                }
            }

            // NP panel
            NpUpdateLoopIcon();
        }

        private void NpUpdateLoopIcon()
        {
            if (NpLoopIcon == null) return;
            var mode = ThemeManager.LoopMode;
            var activeColor = NpGetIconBrush(true);
            var inactiveColor = NpGetIconBrush(false);
            bool active = mode != LoopMode.Off;
            NpLoopIcon.Stroke = active ? activeColor : inactiveColor;
            if (NpLoopOneIndicator != null)
            {
                NpLoopOneIndicator.Visibility = mode == LoopMode.One ? Visibility.Visible : Visibility.Collapsed;
                NpLoopOneIndicator.Foreground = activeColor;
            }
            if (NpLoopBtn != null)
            {
                NpLoopBtn.ToolTip = mode switch { LoopMode.All => "Loop: All", LoopMode.One => "Loop: One", _ => "Loop: Off" };
                NpSetToggleBg(NpLoopBtn, active);
            }
        }

        private void UpdateShuffleUI()
        {
            if (ShuffleIcon != null)
            {
                var accent = (System.Windows.Media.Brush)FindResource("PlaybarAccentColor");
                var muted = (System.Windows.Media.Brush)FindResource("TextMuted");
                ShuffleIcon.Stroke = _shuffleMode ? accent : muted;
                ShuffleIcon.StrokeThickness = _shuffleMode ? 2.6 : 2.2;

                // Update the button background to clearly show active state
                if (BtnShuffle != null)
                {
                    if (_shuffleMode && accent is System.Windows.Media.SolidColorBrush scb)
                    {
                        var glowColor = scb.Color;
                        glowColor.A = 40; // ~15% opacity
                        BtnShuffle.Background = new System.Windows.Media.SolidColorBrush(glowColor);
                    }
                    else
                    {
                        BtnShuffle.Background = System.Windows.Media.Brushes.Transparent;
                    }
                }
            }

            _miniPlayerWindow?.UpdateShuffleState(_shuffleMode);
        }

        /// <summary>
        /// Picks the next track from the shuffled deck. Rebuilds the deck when exhausted,
        /// ensuring every track plays once before any repeats.
        /// </summary>
        private AudioFileInfo? PickRandomTrack(List<AudioFileInfo> items)
        {
            return _shuffleEngine.PickNext(items, FindCurrentTrack(items));
        }

        private List<AudioFileInfo> GetUpcomingTracks(int maxCount)
        {
            var upcoming = new List<AudioFileInfo>();
            if (maxCount <= 0)
                return upcoming;

            foreach (var queued in _queue)
            {
                if (queued.Status == AudioStatus.Corrupt)
                    continue;
                upcoming.Add(queued);
                if (upcoming.Count >= maxCount)
                    return upcoming;
            }

            var items = _filteredView?.Cast<AudioFileInfo>().ToList();
            if (items == null || items.Count == 0)
                return upcoming;

            if (ThemeManager.LoopMode == LoopMode.One)
            {
                var current = FindCurrentTrack(items);
                if (current != null && current.Status != AudioStatus.Corrupt)
                {
                    while (upcoming.Count < maxCount)
                        upcoming.Add(current);
                }
                return upcoming;
            }

            if (_shuffleMode)
            {
                foreach (var track in PeekShuffleTracks(items, maxCount - upcoming.Count))
                    upcoming.Add(track);
                return upcoming;
            }

            string? currentPath = _player.CurrentFile;
            if (currentPath == null)
                return upcoming;

            int currentIdx = items.FindIndex(f => string.Equals(f.FilePath, currentPath, StringComparison.OrdinalIgnoreCase));
            int nextIdx = currentIdx + 1;
            int scanned = 0;
            bool loopAll = ThemeManager.LoopMode == LoopMode.All;

            while (upcoming.Count < maxCount && scanned < items.Count)
            {
                if (nextIdx >= items.Count)
                {
                    if (!loopAll)
                        break;
                    nextIdx = 0;
                }

                var candidate = items[nextIdx++];
                scanned++;
                if (candidate.Status == AudioStatus.Corrupt)
                    continue;
                upcoming.Add(candidate);
            }

            return upcoming;
        }

        private AudioFileInfo? FindCurrentTrack(List<AudioFileInfo> items)
        {
            string? currentPath = _player.CurrentFile;
            if (currentPath == null)
                return null;

            return items.FirstOrDefault(f => string.Equals(f.FilePath, currentPath, StringComparison.OrdinalIgnoreCase));
        }

        private IEnumerable<AudioFileInfo> PeekShuffleTracks(List<AudioFileInfo> items, int count)
        {
            return _shuffleEngine.PeekUpcoming(
                items,
                count,
                FindCurrentTrack(items),
                ThemeManager.LoopMode == LoopMode.All);
        }

        private void PrevTrack_Click(object sender, RoutedEventArgs e)
        {
            _crossfadeEarlyTriggered = false;
            var now = DateTime.UtcNow;
            bool isPlaying = _player.IsPlaying || _player.IsPaused;

            // If currently playing and more than 1.5s since last prev-click,
            // restart the current song instead of going back
            if (isPlaying && _player.CurrentPosition.TotalSeconds > 3
                && (now - _lastPrevClickTime).TotalSeconds > 1.5)
            {
                _lastPrevClickTime = now;
                _player.Seek(0);
                SeekSlider.Value = 0;
                UpdatePlayerTimeText();
                return;
            }

            _lastPrevClickTime = now;

            // Use playback history to go back to the previously played track
            if (_playHistoryIndex > 0)
            {
                _playHistoryIndex--;
                var prevFile = _playHistory[_playHistoryIndex];
                FileGrid.SelectedItem = prevFile;
                FileGrid.ScrollIntoView(prevFile);
                if (prevFile.Status != AudioStatus.Corrupt)
                {
                    _navigatingHistory = true;
                    PlayFile(prevFile, isManualSkip: true);
                    _navigatingHistory = false;
                }
                return;
            }

            // No history available — fall back to list-based navigation
            var items = _filteredView?.Cast<AudioFileInfo>().ToList();
            if (items == null || items.Count == 0) return;

            int currentIdx = -1;
            if (_player.CurrentFile != null)
                currentIdx = items.FindIndex(f => string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase));
            else if (FileGrid.SelectedItem is AudioFileInfo sel)
                currentIdx = items.IndexOf(sel);

            int prevIdx = currentIdx - 1;
            if (prevIdx < 0) prevIdx = items.Count - 1;

            var prevListFile = items[prevIdx];
            FileGrid.SelectedItem = prevListFile;
            FileGrid.ScrollIntoView(prevListFile);
            if (prevListFile.Status != AudioStatus.Corrupt)
                PlayFile(prevListFile, isManualSkip: true);
        }

        private void NextTrack_Click(object sender, RoutedEventArgs e)
        {
            AdvanceToNextTrack(isManualSkip: true, preserveCrossfadeGuard: false);
        }

        private void AdvanceToNextTrack(bool isManualSkip, bool preserveCrossfadeGuard)
        {
            if (!preserveCrossfadeGuard)
                _crossfadeEarlyTriggered = false;
            // Check queue first
            if (_queue.Count > 0)
            {
                var nextFile = _queue[0];
                _queue.RemoveAt(0);
                if (nextFile.Status != AudioStatus.Corrupt)
                {
                    FileGrid.SelectedItem = nextFile;
                    FileGrid.ScrollIntoView(nextFile);
                    PlayFile(nextFile, isManualSkip, preserveCrossfadeGuard);
                    return;
                }
            }

            var items = _filteredView?.Cast<AudioFileInfo>().ToList();
            if (items == null || items.Count == 0) return;

            if (_shuffleMode)
            {
                var candidate = PickRandomTrack(items);
                if (candidate != null)
                {
                    FileGrid.SelectedItem = candidate;
                    FileGrid.ScrollIntoView(candidate);
                    if (candidate.Status != AudioStatus.Corrupt)
                        PlayFile(candidate, isManualSkip, preserveCrossfadeGuard);
                }
                return;
            }

            int currentIdx = -1;
            if (_player.CurrentFile != null)
                currentIdx = items.FindIndex(f => string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase));
            else if (FileGrid.SelectedItem is AudioFileInfo sel)
                currentIdx = items.IndexOf(sel);

            int nextIdx = currentIdx + 1;
            if (nextIdx >= items.Count) nextIdx = 0;

            var nextInList = items[nextIdx];
            FileGrid.SelectedItem = nextInList;
            FileGrid.ScrollIntoView(nextInList);
            if (nextInList.Status != AudioStatus.Corrupt)
                PlayFile(nextInList, isManualSkip, preserveCrossfadeGuard);
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _player.Volume = (float)(VolumeSlider.Value / 100.0);
            // Persist volume so it's restored across sessions. Skip the save while muted
            // (mute writes 0 to the slider — we want the user's pre-mute level to survive).
            if (!_isMuted)
            {
                ThemeManager.Volume = VolumeSlider.Value;
                ThemeManager.SavePlayOptions();
            }
            if (VolumeLabel != null)
                VolumeLabel.Text = $"{(int)VolumeSlider.Value}%";
            if (VolumeIconPath != null)
            {
                if (VolumeSlider.Value <= 0)
                    VolumeIconPath.Data = System.Windows.Media.Geometry.Parse("M 2,5 L 5,5 L 9,2 L 9,14 L 5,11 L 2,11 Z M 12,5 L 15,8 M 15,5 L 12,8");
                else if (VolumeSlider.Value < 34)
                    VolumeIconPath.Data = System.Windows.Media.Geometry.Parse("M 2,5 L 5,5 L 9,2 L 9,14 L 5,11 L 2,11 Z M 11,6 Q 12.5,8 11,10");
                else if (VolumeSlider.Value < 67)
                    VolumeIconPath.Data = System.Windows.Media.Geometry.Parse("M 2,5 L 5,5 L 9,2 L 9,14 L 5,11 L 2,11 Z M 11,5 Q 13,8 11,11 M 13,3.5 Q 15.5,8 13,12.5");
                else
                    VolumeIconPath.Data = System.Windows.Media.Geometry.Parse("M 2,5 L 5,5 L 9,2 L 9,14 L 5,11 L 2,11 Z M 11,5 Q 13,8 11,11 M 13,3 Q 16,8 13,13 M 15,1 Q 19,8 15,15");
            }
        }

        private void VolumeIcon_Click(object sender, MouseButtonEventArgs e)
        {
            if (_isMuted)
            {
                // Unmute: restore previous volume
                _isMuted = false;
                VolumeSlider.Value = _preMuteVolume;
            }
            else
            {
                // Mute: save current volume and set to 0
                _isMuted = true;
                _preMuteVolume = VolumeSlider.Value;
                VolumeSlider.Value = 0;
            }
        }

        private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // During drag, only update visual position — actual seek happens on release
            // This prevents audio stuttering from rapid seek calls
            UpdateWaveformProgress();
        }

        private void SeekSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Detect thumb click vs track click by checking if the mouse is over the thumb
            var thumb = FindVisualChild<System.Windows.Controls.Primitives.Thumb>(SeekSlider);
            if (thumb != null)
            {
                var pos = e.GetPosition(thumb);
                // Add a small padding around the thumb for easier grabbing
                if (pos.X >= -4 && pos.X <= thumb.ActualWidth + 4 &&
                    pos.Y >= -4 && pos.Y <= thumb.ActualHeight + 4)
                {
                    _isSeeking = true;
                    return; // Let the Slider handle thumb drag normally
                }
            }

            // Track click — compute position and seek immediately to avoid snap-back races
            if (SeekSlider.ActualWidth > 0 && _player.TotalDuration.TotalSeconds > 0)
            {
                double ratio = Math.Clamp(e.GetPosition(SeekSlider).X / SeekSlider.ActualWidth, 0, 1);
                double posSec = ratio * _player.TotalDuration.TotalSeconds;

                SeekSlider.Value = posSec;
                _player.Seek(posSec);
                _lastSeekTime = DateTime.UtcNow;
                _lastSeekTargetPosition = TimeSpan.FromSeconds(posSec);
                _isSeeking = true;

                // Sync NP slider
                NpSeekSlider.ValueChanged -= NpSeekSlider_ValueChanged;
                if (NpSeekSlider.Maximum > 0)
                    NpSeekSlider.Value = posSec;
                NpSeekSlider.ValueChanged += NpSeekSlider_ValueChanged;

                _npCurrentLyricIndex = -1;
                NpUpdateLyricHighlight(_lastSeekTargetPosition);
                UpdateWaveformProgress();
                NpRenderPlaybarStyle();
            }
            e.Handled = true;
        }

        private void SeekSlider_MouseUp(object sender, MouseButtonEventArgs e)
        {
            // For track clicks, the seek already happened on mouse down — just clear the flag.
            // For thumb drags, DragCompleted handles the seek and clears the flag.
            var thumb = FindVisualChild<System.Windows.Controls.Primitives.Thumb>(SeekSlider);
            if (thumb == null || !thumb.IsDragging)
            {
                _isSeeking = false;
                UpdateWaveformProgress();
                NpRenderPlaybarStyle();
            }
        }

        private void SeekSlider_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (_player.TotalDuration.TotalSeconds > 0 && SeekSlider.Maximum > 0)
            {
                double pos = SeekSlider.Value / SeekSlider.Maximum * _player.TotalDuration.TotalSeconds;
                _player.Seek(pos);
                _lastSeekTime = DateTime.UtcNow;
                _lastSeekTargetPosition = TimeSpan.FromSeconds(pos);

                // Sync NP slider
                NpSeekSlider.ValueChanged -= NpSeekSlider_ValueChanged;
                if (NpSeekSlider.Maximum > 0)
                    NpSeekSlider.Value = pos;
                NpSeekSlider.ValueChanged += NpSeekSlider_ValueChanged;

                // Force immediate lyric re-sync
                _npCurrentLyricIndex = -1;
                NpUpdateLyricHighlight(_lastSeekTargetPosition);
            }
            _isSeeking = false;
            UpdateWaveformProgress();
            NpRenderPlaybarStyle();
        }

        private void SeekSlider_MouseMove(object sender, MouseEventArgs e)
        {
            UpdateSeekTooltip(SeekSlider, e);
        }

        private void UpdateSeekTooltip(Slider slider, MouseEventArgs e)
        {
            if (_player.TotalDuration.TotalSeconds <= 0 || slider.ActualWidth <= 0) return;
            double mouseX = e.GetPosition(slider).X;
            double ratio = Math.Clamp(mouseX / slider.ActualWidth, 0, 1);
            var hoverTime = TimeSpan.FromSeconds(ratio * _player.TotalDuration.TotalSeconds);
            slider.ToolTip = FormatTime(hoverTime);
        }

        // Tracks consecutive PlayFile failures so a single bad file doesn't stop the queue
        // and a queue full of bad files doesn't cause unbounded recursion.
        private int _consecutiveFailedPlays;
        private const int MaxConsecutiveFailedPlays = 3;

        // Rapid-click debounce: collapse a burst of PlayFile calls so only the latest target plays.
        // _playFileBusy guards re-entrancy; _pendingPlayRequest holds the last requested track during
        // a play-in-progress; _lastPlayFileTickMs adds a 120 ms cooldown for keyboard/Next-Prev spam.
        private int _playFileBusy;
        private AudioFileInfo? _pendingPlayRequest;
        private bool _pendingIsManualSkip;
        private bool _pendingPreserveCrossfade;
        private long _lastPlayFileTickMs;
        private const int PlayFileCooldownMs = 120;

        // True while a PlayFile load is in flight. PlayFile stops the current track and loads the
        // next decoder on a background thread, so _player.IsPlaying briefly reads false mid-skip.
        // Without this, the 50ms NP/player UI tick flips the play→pause icon backward for that
        // window (the "skip makes the play button look paused, then jumps" report). The icon
        // honours this flag so it keeps showing the playing state through the load.
        private bool _pendingPlaybackVisual;

        private static PlaybackSettingsSnapshot CreatePlaybackSettingsSnapshot()
        {
            return PlaybackSettingsSnapshot.From(new ThemeManagerSettings());
        }

        // Bumped each time a load begins; a backgrounded load discards its result if superseded.
        private int _loadGeneration;

        private void PlayFile(AudioFileInfo file, bool isManualSkip = false, bool preserveCrossfadeGuard = false)
        {
            // Cooldown collapse: if the previous PlayFile finished within the cooldown window,
            // store the new request and let the existing call's tail dispatch it.
            long nowMs = Environment.TickCount64;
            if (System.Threading.Interlocked.Exchange(ref _playFileBusy, 1) == 1)
            {
                _pendingPlayRequest = file;
                _pendingIsManualSkip = isManualSkip;
                _pendingPreserveCrossfade = preserveCrossfadeGuard;
                return;
            }
            if (nowMs - _lastPlayFileTickMs < PlayFileCooldownMs)
            {
                _pendingPlayRequest = file;
                _pendingIsManualSkip = isManualSkip;
                _pendingPreserveCrossfade = preserveCrossfadeGuard;
                System.Threading.Interlocked.Exchange(ref _playFileBusy, 0);
                Dispatcher.BeginInvoke(new Action(DrainPendingPlayRequest), System.Windows.Threading.DispatcherPriority.Background);
                return;
            }

            if (!preserveCrossfadeGuard)
                _crossfadeEarlyTriggered = false;
            _npColorGeneration++;
            // Drop the outgoing track's lyrics NOW (before the background load) so they can't
            // linger or scroll-snap to the top during the load window. NpSetTrack rebuilds them
            // for the new track once it's loaded.
            NpBeginTrackTransition(file.FilePath);
            // We intend to be playing once this load completes; hold the playing-state visual so
            // the icon doesn't flicker to "paused" during the stop+background-load window.
            _pendingPlaybackVisual = true;
            bool asyncHandoff = false;
            try
            {
                // Track playback history for back-button navigation
                if (!_navigatingHistory)
                {
                    // If we navigated back and then play a new track, trim forward history
                    if (_playHistoryIndex >= 0 && _playHistoryIndex < _playHistory.Count - 1)
                        _playHistory.RemoveRange(_playHistoryIndex + 1, _playHistory.Count - _playHistoryIndex - 1);

                    _playHistory.Add(file);
                    _playHistoryIndex = _playHistory.Count - 1;
                }

                var playbackSettings = CreatePlaybackSettingsSnapshot();
                bool normalize = playbackSettings.AudioNormalization;
                bool crossfade = playbackSettings.CrossfadeEnabled;

                // ALWAYS stop current playback cleanly first to prevent audio bleed
                // The crossfade path handles its own stop internally
                // Respect manual-skip setting: skip crossfade on manual Next/Prev if disabled
                if (crossfade && _player.IsPlaying && (!isManualSkip || playbackSettings.CrossfadeOnManualSkip))
                {
                    _player.SetUserVolume((float)(VolumeSlider.Value / 100.0));
                    _player.PlayWithCrossfade(file.FilePath, normalize, playbackSettings);
                    ApplyPlaybackTrackUi(file);
                    _consecutiveFailedPlays = 0;
                    PreloadNextTrackData();
                }
                else
                {
                    // Stop current playback on the UI thread FIRST. This is what guarantees the old
                    // track (and, in gapless mode, its pre-buffered next source) is torn down
                    // before the new load begins — without it the outgoing track keeps playing and
                    // can fire its own TrackFinished mid-skip, double-advancing the shuffle deck.
                    // The decoder load itself still runs on a BACKGROUND thread below so a slow file
                    // (large FLAC, VBR MP3 duration scan, MediaFoundation parse) never freezes the
                    // UI. The busy guard is held until the load completes (released in the
                    // continuation), so loads stay serialized and rapid skips still collapse into
                    // _pendingPlayRequest.
                    _player.Stop();
                    _playerTimer.Stop();
                    _player.SetUserVolume((float)(VolumeSlider.Value / 100.0));
                    int gen = ++_loadGeneration;
                    asyncHandoff = true;
                    System.Threading.Tasks.Task.Run(() =>
                    {
                        Exception? err = null;
                        try { _player.Play(file.FilePath, normalize, playbackSettings); }
                        catch (Exception ex) { err = ex; }

                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                if (gen == _loadGeneration)
                                {
                                    if (err != null)
                                    {
                                        HandlePlayFailure(file, err);
                                    }
                                    else
                                    {
                                        ApplyPlaybackTrackUi(file);
                                        _consecutiveFailedPlays = 0;
                                        PreloadNextTrackData();
                                    }
                                }
                            }
                            finally
                            {
                                _lastPlayFileTickMs = Environment.TickCount64;
                                System.Threading.Interlocked.Exchange(ref _playFileBusy, 0);
                                if (_pendingPlayRequest != null)
                                    Dispatcher.BeginInvoke(new Action(DrainPendingPlayRequest), System.Windows.Threading.DispatcherPriority.Background);
                            }
                        }));
                    });
                }
            }
            catch (Exception ex)
            {
                HandlePlayFailure(file, ex);
            }
            finally
            {
                // The async branch releases the guard in its continuation; only the synchronous
                // paths (crossfade / a pre-load exception) release it here.
                if (!asyncHandoff)
                {
                    _lastPlayFileTickMs = Environment.TickCount64;
                    System.Threading.Interlocked.Exchange(ref _playFileBusy, 0);
                    if (_pendingPlayRequest != null)
                        Dispatcher.BeginInvoke(new Action(DrainPendingPlayRequest), System.Windows.Threading.DispatcherPriority.Background);
                }
            }
        }

        /// <summary>Shared playback-failure handling: auto-skip a few bad files, then surface a dialog.</summary>
        private void HandlePlayFailure(AudioFileInfo file, Exception ex)
        {
            _consecutiveFailedPlays++;
            string fileName = file?.FileName ?? Path.GetFileName(file?.FilePath ?? "(unknown)");
            string filePath = file?.FilePath ?? "";

            if (_consecutiveFailedPlays < MaxConsecutiveFailedPlays)
            {
                // Auto-advance past the bad file so a single unsupported track doesn't halt the queue.
                System.Diagnostics.Debug.WriteLine($"[PlayFile] '{fileName}' failed ({ex.GetType().Name}: {ex.Message}) — auto-skipping ({_consecutiveFailedPlays}/{MaxConsecutiveFailedPlays})");
                Dispatcher.BeginInvoke(new Action(() => NextTrack_Click(this, new RoutedEventArgs())));
            }
            else
            {
                _consecutiveFailedPlays = 0;
                // Give up — clear the optimistic playing visual so the icon reflects reality.
                _pendingPlaybackVisual = false;
                ErrorDialog.Show(
                    "Playback Error",
                    $"Cannot play this file:\n{fileName}\n\n{ex.Message}\n\nPath: {filePath}",
                    this);
            }
        }

        // Replays the latest queued PlayFile target after a busy/cooldown collapse.
        private void DrainPendingPlayRequest()
        {
            var pending = _pendingPlayRequest;
            if (pending == null) return;
            bool manual = _pendingIsManualSkip;
            bool preserve = _pendingPreserveCrossfade;
            _pendingPlayRequest = null;
            PlayFile(pending, manual, preserve);
        }

        private void ApplyPlaybackTrackUi(AudioFileInfo file, bool updateQueuePopup = false, bool deferHeavyUi = true)
        {
            // Load finished and real playback state is now established — drop the optimistic
            // playing-state visual used to suppress the mid-skip icon flicker.
            _pendingPlaybackVisual = false;

            // Record previous track stats before switching
            RecordPlaybackStats();

            double duration = _player.TotalDuration.TotalSeconds;
            _cachedPositionSec = _player.CurrentPosition.TotalSeconds;
            _cachedDurationSec = duration;
            _cachedPositionTime = DateTime.UtcNow;
            _isPlayingCached = _player.IsPlaying;
            _playbackStartTime = DateTime.UtcNow;
            _playbackTrack = file;

            // Update taskbar description
            string artist = string.IsNullOrEmpty(file.Artist) ? "Unknown Artist" : file.Artist;
            string title = string.IsNullOrEmpty(file.Title) ? file.FileName ?? "Unknown" : file.Title;
            TaskbarInfo.Description = $"{title} — {artist}";

            SeekSlider.Maximum = duration;
            if (!_isSeeking)
                SeekSlider.Value = Math.Clamp(_cachedPositionSec, 0, Math.Max(0, duration));

            NpSeekSlider.ValueChanged -= NpSeekSlider_ValueChanged;
            NpSeekSlider.Maximum = duration;
            NpSeekSlider.Value = Math.Clamp(_cachedPositionSec, 0, Math.Max(0, duration));
            NpSeekSlider.ValueChanged += NpSeekSlider_ValueChanged;

            _playerTimer.Start();
            UpdatePlayerTimeText();

            // Cheap and essential: the play/pause icon + filename must reflect the new track
            // immediately. Keep this OUT of the deferred block below — that block runs at
            // Background priority, which the per-frame CompositionTarget.Rendering lyric loop
            // (render priority) can starve during gapless/timed-lyric playback, leaving the
            // playbar frozen on the old track ("the next song plays but the UI doesn't update").
            UpdatePlayerUI();

            // Heavy UI rebuilds. Normally deferred so rapid manual skips feel instant. For
            // automatic transitions (gapless) the caller passes deferHeavyUi:false so the full
            // refresh runs synchronously and can't be starved by the render loop.
            var applyHeavyUi = new Action(() =>
            {
                DrawWaveformBackground();

                _currentSpectrogramFile = file;
                SpectrogramTitle.Text = BuildSpectrogramTitle(file);

                ResetVisualizerForTrackChange();

                _discord.UpdatePresence(file.Artist, file.Title, file.FileName,
                    _player.TotalDuration, _player.CurrentPosition, false);
                _lastScrobbleError = null; // fresh track — drop any stale failure from the last one
                _scrobbler.OnTrackStarted(file, _player.TotalDuration.TotalSeconds);
                UpdateScrobbleWidgetVisual();
                _smtc?.UpdateNowPlayingFromTags(file.FilePath);
                _smtc?.UpdatePlaybackState(true, false);

                ObserveUiTask(UpdateAlbumCoverAsync(file.FilePath, _npColorGeneration), nameof(UpdateAlbumCoverAsync));
                ObserveUiTask(LoadMainCoverColors(file.FilePath), nameof(LoadMainCoverColors));
            });

            if (deferHeavyUi)
                Dispatcher.BeginInvoke(applyHeavyUi, DispatcherPriority.Background);
            else
                applyHeavyUi();

            if (_npVisible)
            {
                if (IsNowPlayingUiActive())
                {
                    NpSetTrack(file);
                    NpResumeVisibleWork(forceReloadLyrics: false, forceLyricResync: true);
                    if (updateQueuePopup)
                        Dispatcher.BeginInvoke(new Action(() => NpUpdateQueuePopup()), DispatcherPriority.Background);
                }
                else
                {
                    _npPendingVisibleRefresh = true;
                    _npLyricsNeedCatchUp = true;
                    NpSuspendVisibleWork(markPendingRefresh: false);
                }
            }
        }

        private void ResetVisualizerForTrackChange()
        {
            Array.Clear(_vizSmoothed, 0, _vizSmoothed.Length);
            Array.Clear(_vizBarValues, 0, _vizBarValues.Length);
            // Drop the previous song's trailing samples so the new track's visualizer starts clean
            // instead of lagging/garbling on the old data for the first second or two.
            _player.ResetVisualizerCapture();

            if (_npVisible)
            {
                if (_npVisualizerEnabled)
                    NpStartVisualizer();
                else
                    StopVisualizer();
                return;
            }

            if (_visualizerMode)
                StartVisualizer();
        }

        private void PlayFile_Click(object sender, RoutedEventArgs e)
        {
            if (FileGrid.SelectedItem is AudioFileInfo file)
                PlayFile(file, isManualSkip: true);
        }

        private void FileGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FileGrid.SelectedItem is AudioFileInfo file)
                PlayFile(file, isManualSkip: true);
        }

        /// <summary>
        /// Handles horizontal scrolling in the DataGrid via touchpad/Shift+scroll.
        /// WPF DataGrid doesn't natively handle horizontal scroll gestures from precision touchpads.
        /// </summary>
        private void FileGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Find the ScrollViewer inside the DataGrid
            var scrollViewer = FindVisualChild<ScrollViewer>(FileGrid);
            if (scrollViewer == null) return;

            // Shift+scroll → horizontal scroll
            if (Keyboard.Modifiers == ModifierKeys.Shift)
            {
                ScrollFileGridHorizontally(scrollViewer, -e.Delta);
                e.Handled = true;
                return;
            }

            // Suppress small vertical scroll events that arrive during a touchpad horizontal swipe
            // (touchpads often send both horizontal + tiny vertical deltas simultaneously)
            if ((DateTime.UtcNow - _lastHorizontalScrollTime).TotalMilliseconds < 300 &&
                Math.Abs(e.Delta) < 96)
            {
                RestoreFileGridVerticalOffsetDuringHorizontalGesture(scrollViewer);
                e.Handled = true;
            }
        }

        /// <summary>
        /// Finds a child of the specified type in the visual tree.
        /// </summary>
        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild) return typedChild;
                var found = FindVisualChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        private void PlayerTimer_Tick(object? sender, EventArgs e)
        {
            // Cache position for smooth waveform interpolation
            _cachedPositionSec = _player.CurrentPosition.TotalSeconds;
            _cachedDurationSec = _player.TotalDuration.TotalSeconds;
            _cachedPositionTime = DateTime.UtcNow;
            _isPlayingCached = _player.IsPlaying;

            // Add cooldown after seek to let NAudio catch up, prevents snap-back
            bool seekCooldown = (DateTime.UtcNow - _lastSeekTime).TotalMilliseconds < 500;
            if (_isSeeking && Mouse.LeftButton != MouseButtonState.Pressed && !seekCooldown)
                _isSeeking = false;
            if (!_isSeeking && !seekCooldown && _cachedDurationSec > 0)
            {
                SeekSlider.Value = _cachedPositionSec;
                UpdateWaveformProgress();
            }

            UpdatePlayerTimeText();

            var playbackSettings = CreatePlaybackSettingsSnapshot();

            // Scrobble threshold check (Last.fm / Libre.fm / ListenBrainz)
            _scrobbler.OnPositionUpdate(_player.CurrentPosition.TotalSeconds);

            // Discord Rich Presence — service handles its own throttling
            if (_discord.IsEnabled)
            {
                var discordFile = _player.CurrentFile != null
                    ? _files.FirstOrDefault(f => string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase))
                    : null;
                _discord.UpdatePresence(discordFile?.Artist, discordFile?.Title, discordFile?.FileName,
                    _player.TotalDuration, _player.CurrentPosition, false);
            }

            // Gapless pre-buffer: prepare the next track when <5s remaining (earlier than the old
            // 3s) so the next decoder — incl. its background normalization scan — is ready in time
            // for a seamless switch even on slower files.
            if (_player.IsGaplessActive && !_player.IsGaplessPrepared && !_gaplessPrepInFlight
                && _cachedDurationSec > 0 && _cachedDurationSec - _cachedPositionSec < 5.0
                && _cachedDurationSec - _cachedPositionSec > 0.1
                && ThemeManager.AutoPlayNext)
            {
                var nextTrack = GetNextTrackForGapless();
                AudioQualityChecker.Services.GaplessTrace.Log(
                    $"Tick: gapless prep trigger (remaining={_cachedDurationSec - _cachedPositionSec:F1}s); nextTrack={(nextTrack != null ? AudioQualityChecker.Services.GaplessTrace.Name(nextTrack.FilePath) : "NULL")}");
                if (nextTrack != null)
                {
                    // Latch BEFORE scheduling so the next ticks (every 50ms) don't pile on a second
                    // task while this one is still running its decode + normalization scan. Without
                    // this the swarm of overlapping prepares keeps _gaplessNext nulled and the track
                    // switch loses the bookkeeping that updates CurrentFile / the UI.
                    _gaplessPrepInFlight = true;
                    _ = System.Threading.Tasks.Task.Run(() =>
                    {
                        try { _player.PrepareGapless(nextTrack.FilePath, playbackSettings.AudioNormalization, playbackSettings); }
                        catch { /* gapless prep failed — normal TrackFinished will handle it */ }
                        finally { _gaplessPrepInFlight = false; }
                    });
                }
            }

            // Early crossfade: start next track before current one ends so there is real overlap
            // Add 100ms safety buffer to avoid cutting off the end if the timer fires late
            double remaining = _cachedDurationSec - _cachedPositionSec;
            double crossfadeDuration = playbackSettings.CrossfadeDurationSeconds;
            if (playbackSettings.CrossfadeEnabled && ThemeManager.AutoPlayNext
                && _player.IsPlaying && !_player.IsGaplessActive
                && !_crossfadeEarlyTriggered
                && _cachedDurationSec >= crossfadeDuration * 2  // track must be longer than 2× fade
                && remaining > 0.5 && remaining <= crossfadeDuration + 0.1)
            {
                _crossfadeEarlyTriggered = true;
                AdvanceToNextTrack(isManualSkip: false, preserveCrossfadeGuard: true);
            }
        }

        private DateTime _playbackStartTime;
        private AudioFileInfo? _playbackTrack;

        private void RecordPlaybackStats()
        {
            if (_playbackTrack == null) return;
            double elapsed = (DateTime.UtcNow - _playbackStartTime).TotalSeconds;
            if (elapsed > 0.5)
            {
                LocalStatsCollector.RecordTrackPlayed(_playbackTrack, elapsed);
                ThemeManager.TotalListeningSecondsLifetime += elapsed;
                // Persist off the hot path: a track transition must not block on a full
                // options-file write. SavePlayOptions is lock-guarded so this is race-safe.
                _ = System.Threading.Tasks.Task.Run(() => ThemeManager.SavePlayOptions());
            }
            _playbackTrack = null;
        }

        private void Player_PlaybackStopped(object? sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                // Guard against spurious stop events while audio is still playing
                if (_player.IsPlaying)
                {
                    if (!_playerTimer.IsEnabled)
                        _playerTimer.Start();
                    // Ensure playbar animation stays alive
                    StartWaveformAnimation();
                    RenderPlaybarAnim();
                    return;
                }
                // If paused, keep animation but stop timer
                if (_player.IsPaused)
                {
                    return;
                }
                _playerTimer.Stop();
                UpdatePlayerUI();
                if (!ThemeManager.MainColorMatchEnabled)
                    RestoreMainColorMatch();
                RecordPlaybackStats();
            });
        }

        private void Player_TrackFinished(object? sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                RecordPlaybackStats();

                // Debounce: prevent double-fire from NAudio race conditions
                if ((DateTime.UtcNow - _lastTrackFinishedTime).TotalMilliseconds < 2000) return;
                _lastTrackFinishedTime = DateTime.UtcNow;

                // If gapless handled the transition, don't also do a normal play
                if (_player.IsGaplessActive && _player.IsPlaying) return;

                // If early crossfade already advanced to the next track, suppress double-advance
                if (_crossfadeEarlyTriggered && _player.IsPlaying)
                {
                    _crossfadeEarlyTriggered = false;
                    return;
                }
                _crossfadeEarlyTriggered = false;

                if (!ThemeManager.AutoPlayNext) return;

                // If queue has items, play from queue first. Skip past corrupt entries
                // instead of abandoning the queue when the head item is bad.
                while (_queue.Count > 0)
                {
                    var nextFile = _queue[0];
                    _queue.RemoveAt(0);
                    if (nextFile.Status == AudioStatus.Corrupt) continue;
                    FileGrid.SelectedItem = nextFile;
                    FileGrid.ScrollIntoView(nextFile);
                    PlayFile(nextFile);
                    _currentSpectrogramFile = nextFile;
                    SpectrogramTitle.Text = BuildSpectrogramTitle(nextFile);
                    return;
                }

                // Loop One: replay the current track
                if (ThemeManager.LoopMode == LoopMode.One)
                {
                    string? currentFile = _player.CurrentFile;
                    if (currentFile != null)
                    {
                        var currentTrack = _files.FirstOrDefault(f =>
                            string.Equals(f.FilePath, currentFile, StringComparison.OrdinalIgnoreCase));
                        if (currentTrack != null && currentTrack.Status != AudioStatus.Corrupt)
                        {
                            PlayFile(currentTrack);
                            return;
                        }
                    }
                }

                // Otherwise find current file in the filtered view and play next
                var items = _filteredView?.Cast<AudioFileInfo>().ToList();
                if (items == null || items.Count == 0) return;

                // Shuffle mode: pick a random track
                if (_shuffleMode)
                {
                    var randomTrack = PickRandomTrack(items);
                    if (randomTrack != null)
                    {
                        FileGrid.SelectedItem = randomTrack;
                        FileGrid.ScrollIntoView(randomTrack);
                        PlayFile(randomTrack);
                        _currentSpectrogramFile = randomTrack;
                        SpectrogramTitle.Text = BuildSpectrogramTitle(randomTrack);
                    }
                    return;
                }

                int currentIdx = -1;
                string? currentPath = _player.CurrentFile;
                if (currentPath != null)
                {
                    currentIdx = items.FindIndex(f => string.Equals(f.FilePath, currentPath, StringComparison.OrdinalIgnoreCase));
                }

                int nextIdx = currentIdx + 1;
                // Walk forward (with optional wrap) past corrupt entries so a single bad file
                // doesn't halt automatic advance. Bound the search to one full pass through
                // the list to avoid an infinite loop when every track is corrupt.
                AudioFileInfo? nextInList = null;
                int scanned = 0;
                bool loopAll = ThemeManager.LoopMode == LoopMode.All;
                while (scanned < items.Count)
                {
                    if (nextIdx >= items.Count)
                    {
                        if (loopAll) nextIdx = 0;
                        else return; // end of list, no looping
                    }
                    var candidate = items[nextIdx];
                    if (candidate.Status != AudioStatus.Corrupt)
                    {
                        nextInList = candidate;
                        break;
                    }
                    nextIdx++;
                    scanned++;
                }
                if (nextInList == null) return;

                FileGrid.SelectedItem = nextInList;
                FileGrid.ScrollIntoView(nextInList);
                PlayFile(nextInList);
                // Update spectrogram/visualizer title for the new track
                _currentSpectrogramFile = nextInList;
                SpectrogramTitle.Text = BuildSpectrogramTitle(nextInList);
            });
        }

        private void UpdatePlayerUI()
        {
            // Treat an in-flight skip as "playing" so the icon doesn't flip backward mid-load.
            if (_player.IsPlaying || _pendingPlaybackVisual)
            {
                PlayIcon.Visibility = Visibility.Collapsed;
                PauseIcon.Visibility = Visibility.Visible;
            }
            else
            {
                PlayIcon.Visibility = Visibility.Visible;
                PauseIcon.Visibility = Visibility.Collapsed;
            }

            PlayerFileText.Text = _player.CurrentFile != null
                ? IOPath.GetFileName(_player.CurrentFile)
                : "";

            UpdatePlayerTimeText();
        }

        private void UpdatePlayerTimeText()
        {
            var cur = _player.CurrentPosition;
            var tot = _player.TotalDuration;
            PlayerTimeText.Text = $"{FormatTime(cur)} / {FormatTime(tot)}";
        }

        private static string FormatTime(TimeSpan ts)
        {
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            return $"{ts.Minutes}:{ts.Seconds:D2}";
        }

        // ─── Gapless Playback ───

        /// <summary>
        /// Determines the next track that gapless should pre-buffer, using the same
        /// logic as Player_TrackFinished (queue → shuffle → sequential).
        /// </summary>
        private AudioFileInfo? GetNextTrackForGapless()
        {
            return GetUpcomingTracks(1).FirstOrDefault();
        }

        private void Player_GaplessTrackChanged(object? sender, EventArgs e)
        {
            AudioQualityChecker.Services.GaplessTrace.Log("Player_GaplessTrackChanged: handler entered, queuing UI update");
            Dispatcher.InvokeAsync(() =>
            {
                _lastTrackFinishedTime = DateTime.UtcNow; // prevent TrackFinished from double-firing

                // Dequeue if the gapless track came from the queue
                if (_queue.Count > 0)
                {
                    var expected = _queue[0];
                    if (string.Equals(expected.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase))
                        _queue.RemoveAt(0);
                }

                // Find the new file in the grid and update UI
                var newFile = _files.FirstOrDefault(f =>
                    string.Equals(f.FilePath, _player.CurrentFile, StringComparison.OrdinalIgnoreCase));
                AudioQualityChecker.Services.GaplessTrace.Log(
                    $"Player_GaplessTrackChanged: UI update running; CurrentFile={AudioQualityChecker.Services.GaplessTrace.Name(_player.CurrentFile)}; matchInFiles={(newFile != null)}; filesCount={_files.Count}");
                if (newFile != null)
                {
                    _npColorGeneration++;
                    NpAdvanceBackgroundAnimationOnSongChange();
                    FileGrid.SelectedItem = newFile;
                    FileGrid.ScrollIntoView(newFile);
                    // Gapless is an automatic transition (no rapid-skip burst to stay snappy for),
                    // so apply the full UI synchronously — the deferred/Background path can be
                    // starved by the render-priority lyric loop, which left the UI stuck.
                    ApplyPlaybackTrackUi(newFile, updateQueuePopup: true, deferHeavyUi: false);
                    AudioQualityChecker.Services.GaplessTrace.Log("Player_GaplessTrackChanged: ApplyPlaybackTrackUi COMPLETED");
                }
            });
        }

    }
}
