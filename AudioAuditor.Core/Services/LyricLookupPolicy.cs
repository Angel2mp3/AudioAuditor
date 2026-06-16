using System.Diagnostics;

namespace AudioQualityChecker.Services;

internal sealed record LyricProviderRequest(
    string Name,
    Func<CancellationToken, Task<LyricsResult>> Fetch,
    TimeSpan Timeout);

internal static class LyricLookupPolicy
{
    public static async Task<LyricsResult> ResolveAsync(
        LyricsResult localFallback,
        IReadOnlyList<LyricProviderRequest> providers,
        double trackDurationSeconds,
        bool avoidCensoredLyrics,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        LyricsResult? censoredFallback = null;
        LyricsResult? plainOnlineFallback = null;

        foreach (var provider in providers)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var providerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (provider.Timeout > TimeSpan.Zero)
                    providerCts.CancelAfter(provider.Timeout);

                var result = await provider.Fetch(providerCts.Token).ConfigureAwait(false);
                if (!result.HasLyrics)
                    continue;
                if (LyricService.IsClearlyMismatchedTimedLyrics(result, trackDurationSeconds))
                    continue;

                if (avoidCensoredLyrics && LyricService.IsLikelyCensored(result))
                {
                    censoredFallback ??= result;
                    continue;
                }

                if (result.IsTimed)
                    return result;

                plainOnlineFallback ??= result;
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw;
                Debug.WriteLine($"[LyricLookupPolicy.{provider.Name}] provider timed out");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LyricLookupPolicy.{provider.Name}] {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (plainOnlineFallback != null) return plainOnlineFallback;
        if (censoredFallback != null) return censoredFallback;
        return localFallback.HasLyrics ? localFallback : LyricsResult.Empty;
    }
}
