namespace AudioQualityChecker.Services
{
    public static partial class ThemeManager
    {
        // ─── Discord Rich Presence ───

        public static bool DiscordRpcEnabled { get; set; }

        // The presence service moved to Core, so these mirror onto AudioAuditorSettings —
        // same shim the offline-mode and lyric flags already use.
        private static string _discordRpcClientId = "";
        public static string DiscordRpcClientId
        {
            get => _discordRpcClientId;
            set
            {
                _discordRpcClientId = value;
                AudioQualityChecker.AudioAuditorSettings.DiscordRpcClientId = value;
            }
        }

        private static string _discordRpcDisplayMode = "TrackDetails";
        public static string DiscordRpcDisplayMode
        {
            get => _discordRpcDisplayMode;
            set
            {
                _discordRpcDisplayMode = value;
                AudioQualityChecker.AudioAuditorSettings.DiscordRpcDisplayMode = value;
            }
        }

        private static bool _discordRpcShowElapsed = true;
        public static bool DiscordRpcShowElapsed
        {
            get => _discordRpcShowElapsed;
            set
            {
                _discordRpcShowElapsed = value;
                AudioQualityChecker.AudioAuditorSettings.DiscordRpcShowElapsed = value;
            }
        }
        public static string AcoustIdApiKey { get; set; } = "";
        public static string DiscogsToken { get; set; } = "";
        public static string FanartTvApiKey { get; set; } = "";

        // ─── Streaming-link lookup (embed a track URL in the Comment) ───
        public static string SpotifyClientId { get; set; } = "";
        public static string SpotifyClientSecret { get; set; } = "";
        public static string YouTubeApiKey { get; set; } = "";

        // ─── Last.fm Scrobbling ───

        public static bool LastFmEnabled { get; set; }

        // Mirrored too: the presence service uses it for the album-art lookup.
        private static string _lastFmApiKey = "";
        public static string LastFmApiKey
        {
            get => _lastFmApiKey;
            set
            {
                _lastFmApiKey = value;
                AudioQualityChecker.AudioAuditorSettings.LastFmApiKey = value;
            }
        }
        public static string LastFmApiSecret { get; set; } = "";
        public static string LastFmSessionKey { get; set; } = "";
        public static string LastFmUsername { get; set; } = "";

        // ─── Libre.fm Scrobbling (Audioscrobbler 2.0) ───

        public static bool LibreFmEnabled { get; set; }
        public static string LibreFmApiKey { get; set; } = "";
        public static string LibreFmApiSecret { get; set; } = "";
        public static string LibreFmSessionKey { get; set; } = "";
        public static string LibreFmUsername { get; set; } = "";

        // ─── ListenBrainz Scrobbling ───

        public static bool ListenBrainzEnabled { get; set; }
        public static string ListenBrainzUserToken { get; set; } = "";
        public static string ListenBrainzUsername { get; set; } = "";

        // ─── Maloja Scrobbling (self-hosted; ListenBrainz-compatible endpoint) ───

        public static bool MalojaEnabled { get; set; }
        public static string MalojaServerUrl { get; set; } = "";
        public static string MalojaApiKey { get; set; } = "";
        public static string MalojaUsername { get; set; } = "";

        // ─── System Media Transport Controls (SMTC) ───
        // Publishes now-playing + a live timeline to Windows' media session. Besides driving
        // overlays (FluentFlyout, the Win+G bar, volume flyout), a complete SMTC session is what
        // makes AudioAuditor visible to Pano Scrobbler desktop — which reads the OS session and
        // scrobbles from it. Independent of the built-in Last.fm/Libre.fm/etc. scrobblers.
        public static bool SystemMediaControlsEnabled { get; set; } = true;

        // ─── Global scrobble controls ───

        public static bool PauseScrobbling { get; set; }
        public static int ScrobbleAtPercent { get; set; } = 50;
        public static int ScrobbleAtSeconds { get; set; } = 240;
        public static int MinScrobbleTrackSeconds { get; set; } = 30;
        public static string ScrobbleBlacklist { get; set; } = "";
    }
}
