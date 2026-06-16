using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using AudioQualityChecker.Models;
using AudioQualityChecker.Services;

namespace AudioQualityChecker
{
    /// <summary>
    /// Window-state lifecycle and animation-occlusion management for the main window: opening the
    /// mini player, window activate/deactivate/state-change handling, the occlusion-check timer, and
    /// other-app-fullscreen detection (so animations pause when fully covered). The
    /// <see cref="MainWindow.PauseAnimations"/>/<see cref="MainWindow.ResumeAnimations"/> bridge
    /// methods stay in MainWindow.xaml.cs, as do the shared occlusion fields they coordinate on.
    /// </summary>
    public partial class MainWindow
    {
        // ═══════════════════════════════════════════
        //  Mini Player
        // ═══════════════════════════════════════════

        private void MiniPlayer_Click(object sender, RoutedEventArgs e)
        {
            if (_miniPlayerWindow != null)
            {
                _miniPlayerWindow.Close();
                return;
            }

            _miniPlayerWindow = new MiniPlayerWindow(
                _player,
                onPrev: () => Dispatcher.Invoke(() => PrevTrack_Click(null!, new RoutedEventArgs())),
                onNext: () => Dispatcher.Invoke(() => NextTrack_Click(null!, new RoutedEventArgs())),
                onRestore: () =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        WindowState = WindowState.Normal;
                        Activate();
                        ShowInTaskbar = true;
                    });
                },
                getCurrentTrack: () =>
                {
                    if (FileGrid.SelectedItem is AudioFileInfo file) return file;
                    return _files.FirstOrDefault(f => f.FilePath == _player.CurrentFile);
                },
                onToggleVisualizer: () => Dispatcher.Invoke(RefreshVisualizerOwnershipAfterMiniChange),
                onToggleColorMatch: () =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (_miniPlayerWindow != null)
                        {
                            if (ThemeManager.MainColorMatchEnabled && _mainAlbumPrimary != default)
                                _miniPlayerWindow.ApplyColorMatch(_mainAlbumPrimary, _mainAlbumSecondary);
                            else
                                _miniPlayerWindow.ClearColorMatch();
                        }
                    });
                },
                onToggleShuffle: () => Dispatcher.Invoke(() => Shuffle_Click(null!, new RoutedEventArgs())));

            // Apply current theme colors immediately
            if (ThemeManager.MainColorMatchEnabled && _mainAlbumPrimary != default)
                _miniPlayerWindow.ApplyColorMatch(_mainAlbumPrimary, _mainAlbumSecondary);

            _miniPlayerWindow.Closed += (_, _) =>
            {
                _miniPlayerWindow = null;
                RefreshVisualizerOwnershipAfterMiniChange();
            };
            _miniPlayerWindow.Show();
            _miniPlayerWindow.SetExternalVisualizerSuspended(_npVisible);
            RefreshVisualizerOwnershipAfterMiniChange();
        }

        private void RefreshVisualizerOwnershipAfterMiniChange()
        {
            var owner = NowPlayingWorkCoordinator.ResolveVisualizerOwner(CaptureNowPlayingWorkState());
            if (owner == VisualizerSurfaceOwner.MiniPlayer)
            {
                StopVisualizer();
                return;
            }

            if (owner == VisualizerSurfaceOwner.Main)
                StartVisualizer();
        }

        // ═══════════════════════════════════════════
        //  Animation Occlusion Pause
        // ═══════════════════════════════════════════

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        private void OnWindowActivated(object? sender, EventArgs e)
        {
            _occlusionCheckTimer?.Stop();
            _occlusionCheckTimer = null;

            // Only treat the occlusion flag as cleared if we can actually resume now. If the
            // window is still minimized (Activated can fire before StateChanged lands), DON'T
            // consume the flag here — otherwise StateChanged→Normal would see it already false
            // and never resume, leaving NP frozen. We defer to OnWindowStateChanged instead.
            if (IsNowPlayingUiActive() || !_npVisible)
            {
                if (_isPausedForOcclusion)
                {
                    _isPausedForOcclusion = false;
                    ResumeAnimations();
                }
                else if (_npVisible && (_npUpdateTimer == null || !_npUpdateTimer.IsEnabled))
                {
                    // Safety net: NP is visible/active but its update timer isn't running.
                    NpResumeVisibleWork(forceReloadLyrics: _npPendingVisibleRefresh, forceLyricResync: true);
                }
            }
        }

        private void OnWindowDeactivated(object? sender, EventArgs e)
        {
            if (_occlusionCheckTimer != null)
            {
                _occlusionCheckTimer.Stop();
                _occlusionCheckTimer.Tick -= OcclusionCheckTimer_Tick;
            }
            _occlusionCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _occlusionCheckTimer.Tick += OcclusionCheckTimer_Tick;
            _occlusionCheckTimer.Start();
        }

        private void OnWindowStateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                if (!_isPausedForOcclusion)
                {
                    _isPausedForOcclusion = true;
                    PauseAnimations();
                }
            }
            else
            {
                // Restored from minimized (or any non-minimized state change). Always attempt
                // resume regardless of who cleared the occlusion flag first — ResumeAnimations
                // is idempotent (no-ops if NP work is already running). This is the fix for the
                // freeze where Activated consumed the flag before the window was truly active.
                _isPausedForOcclusion = false;
                // Defer until layout settles — canvas ActualWidth/Height can be 0 right after
                // a minimize/restore cycle.
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                    () => ResumeAnimations());
            }
            if (_npVisible)
                NpEnsureLayoutProfileForCurrentWindowState();
            if (_npVisible)
                NpApplyVizPlacement();
        }

        private void OcclusionCheckTimer_Tick(object? sender, EventArgs e)
        {
            if (IsActive) { _occlusionCheckTimer?.Stop(); return; }
            if (IsNowPlayingUiActive())
            {
                if (_isPausedForOcclusion)
                {
                    _isPausedForOcclusion = false;
                    ResumeAnimations();
                }
                _occlusionCheckTimer?.Stop();
                return;
            }

            bool fullscreen = IsAnotherAppFullscreen();
            if (fullscreen && !_isPausedForOcclusion)
            {
                _isPausedForOcclusion = true;
                PauseAnimations(pauseNowPlayingWork: false);
            }
            else if (!fullscreen && _isPausedForOcclusion)
            {
                _isPausedForOcclusion = false;
                ResumeAnimations();
            }
        }

        private bool IsAnotherAppFullscreen()
        {
            try
            {
                IntPtr fg = GetForegroundWindow();
                IntPtr myHwnd = new WindowInteropHelper(this).Handle;
                if (fg == IntPtr.Zero || fg == myHwnd) return false;

                if (!GetWindowRect(fg, out RECT fgRect)) return false;

                IntPtr monitor = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (!GetMonitorInfo(monitor, ref mi)) return false;

                var bounds = mi.rcMonitor;
                return fgRect.Left <= bounds.Left &&
                       fgRect.Top <= bounds.Top &&
                       fgRect.Right >= bounds.Right &&
                       fgRect.Bottom >= bounds.Bottom;
            }
            catch { return false; }
        }
    }
}
