# Changelog

## v1.5.1

### Improvements

- **Major Analysis Speed Boost** — Spectral analysis, BPM detection, and optimizer detection now use direct seeking (WaveStream.Position) instead of reading-and-discarding to skip through audio files. For a typical 5-minute FLAC, this eliminates ~90% of wasted sample decoding
- **FFT Twiddle Factor Cache** — Pre-computes and caches cos/sin values per FFT size instead of recalculating ~15M trig calls per file. Speeds up all FFT-based analysis (spectral, BPM, optimizer)
- **Re-entrant File Adding** — Adding files while a scan is already in progress now works seamlessly instead of showing an "analysis in progress" error. New files join the existing scan with shared progress tracking
- **Clear All Stops Scanning** — Clicking "Clear All" now immediately cancels any in-progress analysis, resets all batch tracking state, and collapses the progress bar
- **Version Info in Settings** — Settings now displays the current app version and the latest version available on GitHub

### New Features

- **BPM Detection Toggle** — BPM detection can now be disabled to speed up analysis when BPM data isn't needed

---

## v1.5.0

### New Features

- **AcoustID Fingerprinting** — Identify unknown tracks via audio fingerprint using the AcoustID/MusicBrainz database. Automatically downloads fpcalc if not found. Configure your API key in Settings → Integrations
- **Fake Stereo Detection Column** — New dedicated "Fake Stereo" column in the DataGrid and all exports. Detects mono-duplicate and near-mono stereo files using inter-channel correlation analysis (thresholds: ≥0.9999 = "Mono Duplicate", ≥0.995 = "Near-Mono"). Toggleable via the Feature Config overlay
- **True Peak Measurement** — Measures inter-sample true peak level (dBTP) for each file using 4× oversampling, displayed in a dedicated column
- **LUFS Measurement** — Calculates integrated loudness (LUFS / LKFS) per ITU-R BS.1770 with K-weighting, displayed in a dedicated column
- **Rip/Encode Quality Detection (Experimental)** — Analyzes audio for signs of bad rips: zero-sector gaps, clicks/pops, stuck samples, and bit truncation. Opt-in via the feature config overlay; column hidden by default
- **Waveform Comparison** — Select two files (Ctrl+Click) and compare their waveforms in a stacked top/bottom layout with a vertical blend slider to overlay them and a horizontal offset slider for alignment. Shows correlation, RMS difference, and peak difference stats
- **Unified AI Detection Column** — The AI column now reflects results from **all** enabled detection sources (standard metadata/byte scan, experimental spectral analysis, and SH Labs API). Previously only standard detection colored the column — now if any model flags a file, the column highlights orange and displays combined results
- **Batch Rename & Organize** — Rename selected files using configurable patterns (`{artist}`, `{title}`, `{track}`, etc.) with collision detection and optional folder organization
- **Duplicate Detection** — Scan loaded files for duplicates by metadata (artist + title) and file fingerprint (size + duration)
- **Metadata Strip Tool** — Strip all metadata tags from selected audio files (removes ID3, Vorbis comments, APE tags, etc.)
- **Playlist Import (M3U / PLS)** — Import `.m3u`, `.m3u8`, and `.pls` playlist files; resolves relative and absolute paths and loads contained audio files
- **Cue Sheet Support** — Import `.cue` files; parses tracks with start/end times and adds them as virtual entries with full analysis
- **Feature Config Overlay** — On first launch of each new version, a configuration overlay lets you enable/disable optional features.
- **Multi-Select in File Grid** — DataGrid now supports extended selection (Ctrl+Click, Shift+Click) for waveform comparison and batch operations

### Improvements

- **Export Service — "Real" Status Label** — Export reports now show "Real" instead of the internal "Valid" enum name for files that pass quality check
- **Themed Metadata Strip Window** — The metadata strip confirmation window now respects the current app theme

### Safety

- **Seek Audio Blast Protection** — Fixed a critical safety issue where seeking during playback (especially with Opus files) could produce an extremely loud burst of white noise static. Root cause: thread-unsafe Position/Read operations in custom audio readers (Opus, DSD, FLAC) allowed corrupted buffer data when the UI thread's seek collided with the audio thread's read. Now protected by 6 layers of safety: thread-safe reader locks with block alignment, WaveOut device-level volume mute during seek, seek generation counter to detect mid-read corruption, mute buffers (~500ms silence after seek), quadratic fade-in ramp, and per-sample hard limiter with NaN/Infinity protection

### Fixes

- **All Features Now Toggleable** — The 7 core analysis features (Silence, Fake Stereo, DR, True Peak, LUFS, Clipping, MQA) that were previously locked to always-on can now be individually toggled off in the feature config overlay. Disabled features are skipped during analysis and their columns are hidden from the results grid
- **Feature Toggle Startup Sync** — Fixed feature toggles not being applied on startup. Previously, disabling a feature and restarting the app would still run it until the feature config overlay was opened and saved again
- **Discord RPC Shows Selected Not Playing** — Fixed Discord Rich Presence updating to the highlighted/selected file in the grid instead of the actually playing track. Now correctly uses the right song thats playing instead of selected one.
- **Batch Rename Crash** — Fixed crash when renaming files with certain special characters in metadata

---

## v1.4.4

### New Features

- **Automatic Update Checker** — AudioAuditor now silently checks GitHub for new releases each time it starts. If a newer version is found, a popup shows the new version number with a link to download it. Enabled by default; can be turned off in Settings → Play Options → "Check for Updates on Startup"
- **CLI Update Notifications** — The CLI now checks for updates in the background while your command runs and prints an update notice at the end if a newer version is available. Pass `--no-update-check` to disable

### Fixes

- **Circle Rings Visualizer — Full 360° Circles** — Fixed the Circle Rings visualizer so bars radiate outward around the full perimeter of each circle (360°) instead of only along the top half. Each of the 5 frequency-band circles now has bars distributed evenly at all angles, matching the original intended design

---

## v1.4.3 & v1.4.2 (they got mixed together)

### New Features

- **Folder Headers in File List** — Files grouped by folder show collapsible folder header rows in the DataGrid
- **Independent Visualizer Theming** — Choose a separate color theme for the visualizer or let it follow the playbar theme
- **SH Labs AI Audio Detection** — Integration with SH Labs' AI music detection API. Analyzes audio files through a Cloudflare proxy to determine if they were generated by AI, returning a prediction, confidence score, and AI type. Limited to 15 scans/day and 100/month on the shared key; falls back to your other selected detection methods when the limit is reached
- **Custom SH Labs API Key** — Bring your own SH Labs API key in Settings → AI Detection. Audio goes directly to SH Labs with no proxy, no rate limits, and no data collection by AudioAuditor. Key is stored locally only
- **Privacy Notice for SH Labs** — A detailed privacy overlay explains exactly what data is sent, where it goes (Cloudflare proxy → SH Labs, or directly with custom key), and what is stored locally (anonymous install ID, cached results, usage counters). Shown once on first enable; reviewable anytime via the ⚠️ icon in Settings
- **Visualizer Full Volume** — New setting that makes the visualizer always respond as if volume is at 100%, even when you lower it. Keeps visuals lively at any volume level
  
### SO MANY VISUALIZERS RAHHH

- **Particle Fountain Visualizer** — New visualizer mode where particles erupt upward from spawn points along the bottom, driven by frequency energy. Particles arc naturally with gravity and air drag, fading and shrinking as they age. Height, speed, and color intensity all react to the music
- **Mirrored Bars Visualizer** — Frequency bars extend both up and down from the center, creating a symmetrical mirror effect. Bottom reflection renders at 60% opacity for a natural mirror look
- **Circle Rings Visualizer** — Five frequency-range circles arranged in a row, each assigned to a different part of the spectrum (sub-bass, bass, mids, upper-mids, highs). Each circle has radiating bars around its perimeter that react to the energy in its assigned frequency band
- **Oscilloscope Visualizer** — Real-time waveform display showing the actual audio signal shape. Renders and starts as a smooth connected line but then changes according to the music playing
- **Abstract Visualizer** — Infinite zoom tunnel with concentric polygon rings/other shapes/lines that scale outward, react to music energy
- **VU Meter Visualizer** — Classic DJ-style stacked block meter with theme-aware gradient colors. 
- **Visualizer Style Cycling** — Click the "Style" button in the visualizer toolbar to cycle between all 7 modes: Bars → Mirror → Particles → Circles → Scope → Abstract → VU Meter. Preference is saved across sessions

### Improvements
- **Rainbow Playbar Fix** — Rainbow Bars playbar theme now properly cycles through all hues in real-time. The waveform gradient, shuffle button, volume slider, and all accent elements animate through the full color spectrum instead of being stuck on green
- **Discord Rich Presence Overhaul** — Now shows elapsed time and song duration progress bar. Fetches album art from Last.fm (when ID is configured). Play/pause state shown now. Automatic reconnection on connection failure. Reduced throttle with instant updates on play/pause state changes, Removed unused display modes ("Listening to Music on AudioAuditor" and "Listening to Music"). "Track Details" is now the default. Status text changed from "Playing" to "Listening". AudioAuditor name is now a clickable link to audioauditor.org. Fixed timer showing countdown instead of elapsed time
- **Winget Installation Option** - Added a new way to install the GUI and or CLI through Winget
- **Experimental AI Detection — 4 New Checks** — Added Spectral Centroid Stability (detects AI's unusually stable tonal balance), Dynamic Uniformity (detects uniform loudness), Peak Saturation (detects hard-clipping used to destroy watermarks in transients), and Crest Factor Homogeneity (detects aggressive uniform limiting applied to suppress dynamic watermarks). Experimental spectral AI detection now runs 7 checks total instead of 3. The two obfuscation-artifact checks (peak saturation, crest factor) are supporting-only flags that cannot trigger detection alone, avoiding false positives on heavily mastered or clipped tracks
- **AI Watermark Detection — UTF-16 Scanning** — Now detects AI service identifiers embedded in UTF-16 LE encoding (commonly found in ID3v2 frames that TagLib cannot parse), catching markers previously missed by ASCII-only scanning
- **AI Watermark Detection — Expanded Markers** — Added Chirp-v4/v5 model identifiers for newer Suno AI models

### Fixes
- **Fixed resource leaks in audio decoder fallback chains** — Reader objects are now properly disposed when an intermediate step throws during format detection, preventing handle/memory leaks after scanning many corrupted files
- - **Light Theme Selected Row Readability** — Fixed selected/playing row text being invisible on the Light theme. DataGrid cells now correctly apply the selection background color so white text is readable
- **Other general fixes & improvements**


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