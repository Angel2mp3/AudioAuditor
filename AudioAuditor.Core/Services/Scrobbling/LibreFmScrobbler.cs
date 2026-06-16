using System;

namespace AudioQualityChecker.Services.Scrobbling
{
    /// <summary>
    /// Libre.fm reuses the Audioscrobbler 2.0 protocol; only the endpoint and profile URL change.
    /// </summary>
    public sealed class LibreFmScrobbler : LastFmScrobbler
    {
        protected override string ApiUrl => "https://libre.fm/2.0/";
        public override string ServiceName => "Libre.fm";
        public override string ProfileUrl =>
            string.IsNullOrEmpty(Username) ? "https://libre.fm" : $"https://libre.fm/user/{Uri.EscapeDataString(Username)}";
    }
}
