# Changelog

## Latest

### New Features
- **Linear frequency scale** — Toggle between logarithmic and linear frequency axis for spectrograms
- **L-R difference channel** — View Left minus Right channel spectrogram to reveal stereo differences; persists across sessions
- **Jump to end** — Zoom into the last 10 seconds of a recording to inspect fade-outs and tail content
- **Thorough clipping analysis** — Detects clipping even when audio has been scaled down by up to 0.5 dB, reported as "SCALED (dB, %)"
- **Improved bitrate analysis** — Avoids simplistic "320 kbps" labeling for files with steep lowpass filters; uses band-energy-drop method
- **Lossless bitrate accuracy** — FLAC/WAV/AIFF files now show their actual file data rate instead of a lossy-equivalent estimate
- **Optimized status in summary** — Status bar now shows count of optimized files alongside real, fake, unknown, corrupted, MQA, and AI
- **Compressed format support** — Full analysis and playback support for MP3, AAC, OGG, OPUS, WMA, M4A, APE, WV, DSF, DFF
- **Custom FLAC decoder** — Managed FLAC decoder for files that NAudio cannot handle natively
- **-130 dB analysis floor** — Spectrogram rendering now extends down to -130 dB for deeper visibility into low-level content

### Fixes
- **Volume icon** — Now shows 4 distinct states: muted (X), low (1 arc), medium (2 arcs), high (3 arcs)
- **Visualizer info stability** — Clicking a different song while playing no longer changes the visualizer title/info; only updates when that song is actually played
- **Shuffle icon sizing** — Enlarged to match other transport control icons
- **Clipping column width** — Expanded to fully display scaled clipping info without requiring manual resize
- **AI detection false positives** — Reduced false positives through improved confidence scoring and DAW/encoder filtering
- **FLAC playback** — Fixed certain FLAC files failing to play or analyze
- **Transport control symbols** — Fixed icons not displaying correctly
