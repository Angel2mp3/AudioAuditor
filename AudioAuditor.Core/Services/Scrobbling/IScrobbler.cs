using System;
using System.Threading;
using System.Threading.Tasks;

namespace AudioQualityChecker.Services.Scrobbling
{
    public interface IScrobbler : IDisposable
    {
        string ServiceName { get; }
        bool IsEnabled { get; }
        bool IsAuthenticated { get; }
        string ProfileUrl { get; }
        string Username { get; }

        /// <summary>
        /// Human-readable reason the most recent submission failed, or null if the last
        /// operation succeeded. Lets the host surface scrobble failures instead of dropping
        /// them silently.
        /// </summary>
        string? LastError { get; }

        Task TrackStartedAsync(ScrobbleTrack track, CancellationToken ct);
        Task TrackStoppedAsync(CancellationToken ct);
        Task ScrobbleAsync(ScrobbleTrack track, DateTimeOffset playedAt, CancellationToken ct);
    }
}
