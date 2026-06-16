using System;
using System.Collections.Generic;
using NAudio.Wave;

namespace AudioQualityChecker.Services
{
    public partial class AudioPlayer
    {
        // ─── Next-track decoder pre-open ───
        //
        // For normal auto-advance (no gapless/crossfade) the next track's decoder was opened COLD
        // only after the current track ended, so every transition paid a decoder-open gap. Here we
        // pre-open the predicted next track's decoder while the current one is still playing (driven
        // by PreloadNextTrackData on a background thread). Play() adopts the warm decoder if the
        // path matches.
        //
        // IMPORTANT: this is deliberately DECOUPLED from Stop(). Stop() stays the immediate,
        // UI-thread teardown that prevents the outgoing track from double-advancing the queue/deck —
        // it must NOT touch the pre-open. The pre-open is disposed only on a prediction miss (inside
        // Play) and on Dispose(). This is NOT gapless concatenation; tracks stay discrete.
        private readonly object _preopenLock = new();
        private DecoderResult? _preopenedResult;
        private string? _preopenedPath;

        /// <summary>
        /// Pre-opens the decoder for <paramref name="filePath"/> so a subsequent Play() of the same
        /// path starts almost instantly. Safe to call repeatedly / from a background thread; a no-op
        /// if already prepared. Preparing a different path disposes the previous one.
        /// </summary>
        public void PrepareNextDecoder(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || _disposed) return;

            lock (_preopenLock)
            {
                if (string.Equals(_preopenedPath, filePath, StringComparison.OrdinalIgnoreCase))
                    return; // already warm
            }

            // Open OUTSIDE the lock — decoder open can be slow and we never want to stall the audio
            // thread (which takes _preopenLock when adopting).
            if (!AudioDecoderFactory.TryOpen(filePath, out var result))
                return;

            lock (_preopenLock)
            {
                DisposePreopenedLocked(); // drop any stale/previous prepared decoder
                if (_disposed)
                {
                    DisposeDecoderResult(result);
                    return;
                }
                _preopenedResult = result;
                _preopenedPath = filePath;
            }
        }

        /// <summary>
        /// Takes the pre-opened decoder if it matches <paramref name="filePath"/>, transferring
        /// ownership to the caller. Returns false on a miss (leaving any stale pre-open in place for
        /// the caller to drop via <see cref="DisposePreparedDecoder"/>).
        /// </summary>
        private bool TryTakePreopenedDecoder(string filePath, out DecoderResult decoded)
        {
            lock (_preopenLock)
            {
                if (_preopenedResult is { } pre &&
                    string.Equals(_preopenedPath, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    decoded = pre;
                    _preopenedResult = null;
                    _preopenedPath = null;
                    return true;
                }
            }
            decoded = default;
            return false;
        }

        /// <summary>
        /// Disposes any pre-opened decoder still held (a prediction miss, or final cleanup). A no-op
        /// right after a successful adopt. Never called from Stop() — see the note above.
        /// </summary>
        public void DisposePreparedDecoder()
        {
            lock (_preopenLock)
                DisposePreopenedLocked();
        }

        private void DisposePreopenedLocked()
        {
            if (_preopenedResult is { } pre)
                DisposeDecoderResult(pre);
            _preopenedResult = null;
            _preopenedPath = null;
        }

        private static void DisposeDecoderResult(DecoderResult d)
        {
            // A single underlying object can appear in several slots (e.g. Opus is both
            // WaveStreamReader and ExtraDisposable). Dispose each distinct instance once.
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            void Dispose(IDisposable? x)
            {
                if (x != null && seen.Add(x))
                {
                    try { x.Dispose(); } catch { }
                }
            }

            Dispose(d.Reader);
            Dispose(d.MfReader);
            Dispose(d.WaveStreamReader as IDisposable);
            Dispose(d.ExtraDisposable);
            Dispose(d.ExtraDisposable2);
        }
    }
}
