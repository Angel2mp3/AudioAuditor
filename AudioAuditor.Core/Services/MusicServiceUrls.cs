using System;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// Builds the search URL for a music service slot.
    ///
    /// Lived twice — once in the WPF ThemeManager and once in ThemeManagerAvalonia — and had
    /// already drifted: only the WPF copy honoured the region-aware setting, and only the WPF
    /// caller handled the "Custom..." slot at all, so a configured custom URL on Avalonia
    /// silently ran a Google search instead. One copy here, both UIs call it.
    ///
    /// Region and the region-aware flag are passed in rather than read from settings so this
    /// stays free of any ThemeManager reference (and stays testable).
    /// </summary>
    public static class MusicServiceUrls
    {
        /// <summary>The slot name that means "use the user's own URL".</summary>
        public const string CustomServiceName = "Custom...";

        /// <summary>
        /// URL for <paramref name="serviceName"/> searching <paramref name="query"/>.
        /// Unknown service names fall back to a web search.
        /// </summary>
        public static string Build(string serviceName, string query,
            string? region = "us", bool regionAware = false)
        {
            string encoded = Uri.EscapeDataString(query);
            string r = string.IsNullOrWhiteSpace(region) ? "us" : region.ToLowerInvariant();

            return serviceName switch
            {
                "Spotify" => $"https://open.spotify.com/search/{encoded}",
                "YouTube Music" => $"https://music.youtube.com/search?q={encoded}",
                "Tidal" => $"https://listen.tidal.com/search?q={encoded}",
                "Qobuz" => regionAware
                    ? $"https://www.qobuz.com/{QobuzRegion(r)}/search/tracks/{encoded}"
                    : $"https://www.qobuz.com/us-en/search/tracks/{encoded}",
                "Amazon Music" => regionAware
                    ? $"https://music.amazon.{AmazonTld(r)}/search/{encoded}"
                    : $"https://music.amazon.com/search/{encoded}",
                "Apple Music" => regionAware && r != "us"
                    ? $"https://music.apple.com/{r}/search?term={encoded}"
                    : $"https://music.apple.com/us/search?term={encoded}",
                "Deezer" => $"https://www.deezer.com/search/{encoded}",
                "SoundCloud" => $"https://soundcloud.com/search?q={encoded}",
                "Bandcamp" => $"https://bandcamp.com/search?q={encoded}",
                "Last.fm" => $"https://www.last.fm/search?q={encoded}",
                _ => $"https://www.google.com/search?q={encoded}"
            };
        }

        /// <summary>
        /// Applies <paramref name="query"/> to a user-supplied URL: substituted for a
        /// <c>{query}</c> placeholder if there is one, appended as a path segment otherwise.
        /// Returns null when no URL is configured, which callers report as an error rather
        /// than navigating somewhere unintended.
        /// </summary>
        public static string? BuildCustom(string? customUrl, string query)
        {
            if (string.IsNullOrWhiteSpace(customUrl)) return null;

            string encoded = Uri.EscapeDataString(query);
            string candidate = customUrl.Contains("{query}")
                ? customUrl.Replace("{query}", encoded)
                : customUrl.TrimEnd('/') + "/" + encoded;

            return Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                ? uri.AbsoluteUri
                : null;
        }

        /// <summary>Metadata lookup sites offered by the tag editor.</summary>
        public enum LookupSite
        {
            MusicBrainz,
            Discogs,
            AllMusic,
            RateYourMusic
        }

        /// <summary>Search URL for a metadata lookup site.</summary>
        public static string Lookup(LookupSite site, string query)
        {
            string encoded = Uri.EscapeDataString(query);

            return site switch
            {
                LookupSite.MusicBrainz => $"https://musicbrainz.org/search?query={encoded}&type=recording",
                LookupSite.Discogs => $"https://www.discogs.com/search/?q={encoded}&type=all",
                LookupSite.AllMusic => $"https://www.allmusic.com/search/all/{encoded}",
                LookupSite.RateYourMusic => $"https://rateyourmusic.com/search?searchterm={encoded}&searchtype=",
                _ => $"https://www.google.com/search?q={encoded}"
            };
        }

        /// <summary>
        /// Best available search text for a track: artist plus title, falling back through
        /// artist plus album and then whichever single field is filled in. Returns null when
        /// nothing is tagged, leaving the caller to use the file name.
        /// </summary>
        public static string? LookupQuery(string? artist, string? title, string? album)
        {
            artist = artist?.Trim() ?? "";
            title = title?.Trim() ?? "";
            album = album?.Trim() ?? "";

            if (artist.Length > 0 && title.Length > 0) return $"{artist} {title}";
            if (artist.Length > 0 && album.Length > 0) return $"{artist} {album}";
            if (title.Length > 0) return title;
            if (artist.Length > 0) return artist;

            return null;
        }

        private static string AmazonTld(string region) => region switch
        {
            "uk" => "co.uk",
            "jp" => "co.jp",
            "au" => "com.au",
            "br" => "com.br",
            "mx" => "com.mx",
            "in" => "in",
            "ca" => "ca",
            "de" => "de",
            "fr" => "fr",
            _ => "com"
        };

        private static string QobuzRegion(string region) => region switch
        {
            "us" => "us-en",
            "uk" => "uk-en",
            "ca" => "ca-en",
            "au" => "au-en",
            "de" => "de-de",
            "fr" => "fr-fr",
            "jp" => "jp-ja",
            "br" => "br-pt",
            "mx" => "mx-es",
            "in" => "in-en",
            _ => "us-en"
        };
    }
}
