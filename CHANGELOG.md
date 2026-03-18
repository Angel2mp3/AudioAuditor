# Changelog

## v1.4.1

### New Features
- **Experimental Spectral AI Detection** — New opt-in setting that uses audio signal analysis to detect AI-generated music. Performs three spectral checks: ultrasonic energy excess (abnormal energy above 16 kHz), high-frequency stereo correlation (unnaturally identical L/R channels above 4 kHz), and spectral regularity (too-smooth spectral patterns across frames). Requires 2+ flags to mark a file as suspicious. Enable in Settings → Experimental or via `--experimental-ai` CLI flag. Note: this is experimental and may produce false positives

### Improvements
- **BPM Detection Overhaul** — Completely rewritten BPM detection using multi-band spectral flux onset detection with harmonic/subharmonic disambiguation. Now analyzes 60 seconds (skipping intros) instead of 30, uses frequency-band weighted analysis (kick/bass bands prioritized), adaptive thresholding, autocorrelation peak picking, and perceptual tempo preference for the 80–160 BPM range. Fixes issues where songs were incorrectly detected at half tempo
- **AI Detection Refined** — Added more verifiable metadata markers and embedded byte patterns (service domains, watermark systems, known tag fields). Removed aggressive heuristics that caused false positives on legitimately tagged files
- **CLI `--experimental-ai` flag** — Enables spectral AI detection for `analyze` and `info` commands from the command line

### Fixes
- **Fixed hi-res lossless false positives** — Files at 48/96/192 kHz sample rates were incorrectly flagged as "Fake" because the algorithm required spectral content up to 90% of Nyquist (e.g. 43 kHz for 96 kHz files), which is far beyond what any music contains. Now uses an absolute frequency floor: if content reaches 19.5 kHz+, the file is considered genuine regardless of sample rate. This also fixes the "24 kbps actual bitrate" reports that appeared on legitimate 24-bit lossless files
- **Reduced lossless Fake threshold** — Lowered the "Fake" bitrate cutoff from ≤160 kbps to ≤128 kbps. Files with estimated source quality between 128–160 kbps now report as "Unknown" instead of "Fake", reducing false positives on recordings with natural high-frequency rolloff
- **Support Links Added** — A **one time** non invasive pop up that shows only the first time audio files were scanned using the program, **after you dismiss it, it will never show again :)** also small non-invasive "Support AudioAuditor ❤" link in footer bar which opens the Ko-fi donate page. **Right-click to dismiss permanently**
- **Other general fixes & improvements I forgot about lol**