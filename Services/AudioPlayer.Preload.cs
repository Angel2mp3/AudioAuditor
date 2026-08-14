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
        // Path currently being opened. _preopenedPath is only set AFTER TryOpen returns, so without
        // this a second caller arriving mid-open sees "not warm yet" and starts its own duplicate
        // open of the same file. For FLAC that open is a full-file decode, so a burst of callers
        // could pile identical decodes onto the thread pool until the app stopped responding.
        private string? _preopenInFlightPath;

        /// <summary>
        /// Pre-opens the decoder for <paramref name="filePath"/> so a subsequent Play() of the same
        /// path starts almost instantly. Safe to call repeatedly / from a background thread; a no-op
        /// if already prepared or if an open for the same path is already running. Preparing a
        /// different path disposes the previous one.
        /// </summary>
        public void PrepareNextDecoder(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || _disposed) return;

            lock (_preopenLock)
            {
                if (string.Equals(_preopenedPath, filePath, StringComparison.OrdinalIgnoreCase))
                    return; // already warm
                if (string.Equals(_preopenInFlightPath, filePath, StringComparison.OrdinalIgnoreCase))
                    return; // an open for this exact path is already running
                _preopenInFlightPath = filePath;
            }

            try
            {
                // Open OUTSIDE the lock — decoder open can be slow and we never want to stall the
                // audio thread (which takes _preopenLock when adopting).
                if (!AudioDecoderFactory.TryOpen(filePath, out var result))
                    return;

                lock (_preopenLock)
                {
                    DisposePreopenedLocked(); // drop any stale/previous prepared decoder
                    if (_disposed)
                    {
                        result.Dispose();
                        return;
                    }
                    _preopenedResult = result;
                    _preopenedPath = filePath;
                }
            }
            finally
            {
                lock (_preopenLock)
                {
                    if (string.Equals(_preopenInFlightPath, filePath, StringComparison.OrdinalIgnoreCase))
                        _preopenInFlightPath = null;
                }
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
                pre.Dispose();
            _preopenedResult = null;
            _preopenedPath = null;
        }
    }
}
