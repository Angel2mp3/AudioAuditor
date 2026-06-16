namespace AudioQualityChecker.Services
{
    public static partial class ThemeManager
    {
        // ─── Discord Rich Presence ───

        public static bool DiscordRpcEnabled { get; set; }
        public static string DiscordRpcClientId { get; set; } = "";
        public static string DiscordRpcDisplayMode { get; set; } = "TrackDetails";
        public static bool DiscordRpcShowElapsed { get; set; } = true;
        public static string AcoustIdApiKey { get; set; } = "";

        // ─── Last.fm Scrobbling ───

        public static bool LastFmEnabled { get; set; }
        public static string LastFmApiKey { get; set; } = "";
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

        // ─── Global scrobble controls ───

        public static bool PauseScrobbling { get; set; }
        public static int ScrobbleAtPercent { get; set; } = 50;
        public static int ScrobbleAtSeconds { get; set; } = 240;
        public static int MinScrobbleTrackSeconds { get; set; } = 30;
        public static string ScrobbleBlacklist { get; set; } = "";
    }
}
