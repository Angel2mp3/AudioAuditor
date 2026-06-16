using AudioQualityChecker.Models;

namespace AudioQualityChecker.Services;

public sealed class ShuffleEngine
{
    private readonly Random _rng;
    private readonly List<AudioFileInfo> _deck = new();
    private int _deckIndex;

    public ShuffleEngine(Random? rng = null)
    {
        _rng = rng ?? new Random();
    }

    public void Reset()
    {
        _deck.Clear();
        _deckIndex = 0;
    }

    public AudioFileInfo? PickNext(IReadOnlyList<AudioFileInfo> items, AudioFileInfo? avoid = null)
    {
        EnsureDeck(items, avoid);
        if (_deck.Count == 0)
            return null;

        return _deck[_deckIndex++];
    }

    public IReadOnlyList<AudioFileInfo> PeekUpcoming(
        IReadOnlyList<AudioFileInfo> items,
        int count,
        AudioFileInfo? avoid = null,
        bool wrap = false)
    {
        if (count <= 0)
            return Array.Empty<AudioFileInfo>();

        EnsureDeck(items, avoid);
        if (_deck.Count == 0)
            return Array.Empty<AudioFileInfo>();

        var upcoming = new List<AudioFileInfo>(Math.Min(count, _deck.Count));
        int idx = Math.Clamp(_deckIndex, 0, _deck.Count);
        while (upcoming.Count < count && idx < _deck.Count)
        {
            var candidate = _deck[idx++];
            if (candidate.Status != AudioStatus.Corrupt)
                upcoming.Add(candidate);
        }

        if (!wrap)
            return upcoming;

        for (int i = 0; i < _deck.Count && upcoming.Count < count; i++)
        {
            var candidate = _deck[i];
            if (candidate.Status != AudioStatus.Corrupt)
                upcoming.Add(candidate);
        }

        return upcoming;
    }

    private void EnsureDeck(IReadOnlyList<AudioFileInfo> items, AudioFileInfo? avoid)
    {
        int playableCount = items.Count(IsPlayable);
        if (playableCount == 0)
        {
            Reset();
            return;
        }

        if (_deck.Count == 0 || _deckIndex >= _deck.Count || _deck.Count != playableCount)
            RebuildDeck(items, avoid);
    }

    private void RebuildDeck(IReadOnlyList<AudioFileInfo> items, AudioFileInfo? avoid)
    {
        _deck.Clear();
        _deck.AddRange(items.Where(IsPlayable));

        for (int i = _deck.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (_deck[i], _deck[j]) = (_deck[j], _deck[i]);
        }

        if (avoid != null && _deck.Count > 1 && SamePath(_deck[0], avoid))
        {
            int swapIndex = _rng.Next(_deck.Count - 1) + 1;
            (_deck[0], _deck[swapIndex]) = (_deck[swapIndex], _deck[0]);
        }

        _deckIndex = 0;
    }

    private static bool IsPlayable(AudioFileInfo file)
    {
        return file.Status != AudioStatus.Corrupt;
    }

    private static bool SamePath(AudioFileInfo left, AudioFileInfo right)
    {
        return string.Equals(left.FilePath, right.FilePath, StringComparison.OrdinalIgnoreCase);
    }
}
