namespace AudioQualityChecker.Services.Scrobbling
{
    public sealed record ScrobbleTrack(
        string Artist,
        string Title,
        string Album,
        double DurationSeconds,
        string? MbId = null);
}
