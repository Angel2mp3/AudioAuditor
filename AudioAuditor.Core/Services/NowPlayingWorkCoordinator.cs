namespace AudioQualityChecker.Services;

public enum VisualizerSurfaceOwner
{
    None,
    Main,
    NowPlaying,
    MiniPlayer
}

public readonly record struct NowPlayingWorkState(
    bool MainWindowVisible,
    bool MainWindowMinimized,
    bool NowPlayingVisible,
    bool NowPlayingVisualizerEnabled,
    bool MiniPlayerVisible,
    bool MiniPlayerMinimized,
    bool MiniPlayerVisualizerEnabled,
    bool MiniPlayerExternallySuspended,
    bool MainVisualizerEnabled,
    bool PlaybackActive)
{
    public bool IsMainWindowActive => MainWindowVisible && !MainWindowMinimized;

    public bool IsNowPlayingUiActive => IsMainWindowActive && NowPlayingVisible;

    public bool IsMiniPlayerUiActive => MiniPlayerVisible && !MiniPlayerMinimized;
}

public static class NowPlayingWorkCoordinator
{
    public static bool CanRunNowPlayingWork(NowPlayingWorkState state) =>
        state.IsNowPlayingUiActive;

    public static bool CanRunMiniPlayerWork(NowPlayingWorkState state) =>
        state.IsMiniPlayerUiActive;

    public static VisualizerSurfaceOwner ResolveVisualizerOwner(NowPlayingWorkState state)
    {
        if (!state.PlaybackActive || !state.IsMainWindowActive)
            return VisualizerSurfaceOwner.None;

        if (state.IsNowPlayingUiActive)
            return state.NowPlayingVisualizerEnabled
                ? VisualizerSurfaceOwner.NowPlaying
                : VisualizerSurfaceOwner.None;

        if (state.IsMiniPlayerUiActive &&
            state.MiniPlayerVisualizerEnabled &&
            !state.MiniPlayerExternallySuspended)
        {
            return VisualizerSurfaceOwner.MiniPlayer;
        }

        return state.MainVisualizerEnabled
            ? VisualizerSurfaceOwner.Main
            : VisualizerSurfaceOwner.None;
    }
}
