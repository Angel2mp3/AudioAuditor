## v1.7.0 (latest)

### This is gonna be a long one lol
### New Features

#### GUI — Spectrogram Tools
- **High-Fidelity spectrogram (16384 FFT, Blackman-Harris window)** — produces sharper frequency detail at the cost of longer render time. Recommended for lossy artifact inspection. Toggle in Settings → Export
- **Scientific color theme (Magma perceptual gradient)** — replaces the default heatmap with a perceptually uniform dark-purple to yellow gradient. Toggle in Settings → Export
- **Compare Spectrograms** — side-by-side full-resolution spectrogram view with **Overlay** (adjustable blend slider) and **Wipe** (draggable vertical splitter) comparison modes
- **View Spectrogram** — right-click any file to open a fullscreen spectrogram window with zoom, pan, and channel controls (mono / L / R / stereo)
- Spectrogram viewer and compare window use the custom window chrome (rounded corners, draggable title bar, themed close button)
- Spectrograms are cached in memory (up to 30 LRU entries) — re-opening a previously viewed spectrogram loads instantly

#### GUI — Offline Mode
- New **Offline Mode** toggle in Settings → Integrations that disables all network calls: lyrics fetching, update checks, SH Labs AI detection, Last.fm scrobbling, lyric translation, Discord Rich Presence
- First-launch dialog asks Online or Offline — Online is highlighted as recommended and pre-selected
- Confirmation popup shown when switching modes in either direction
- `● OFFLINE` badge in the status bar while Offline Mode is active

#### GUI — Crossfade
- More crossfade options: Equal Power, Linear, Natural, and Sequential (no overlap)
- Crossfade duration range expanded from 10 seconds to **15 seconds**
- Default crossfade duration for new installs changed from 3 seconds to **5 seconds** (existing user settings are preserved)
- New **"Crossfade on manual skip"** toggle (Settings → Playback): when disabled, crossfade only triggers at natural song-end auto-advance, not when you press Next/Previous
- **"Crossfade on manual skip"** defaults to **OFF** for new installs

#### GUI — Favorites System
- **Star any file** to mark it as a favorite via the star column or right-click menu
- Favorites always sort to the top and persist in `%APPDATA%\AudioAuditor\favorites.json`
- **Move Up / Move Down** in the right-click Favorites submenu to reorder starred files
- Clear All Favorites and Edit Favorites File buttons in Settings → Cache & Files
- Star column is reorderable and can be hidden via the Column Visibility panel

#### GUI — Lyrics Save & Auto-Save
- **Save Lyrics as .lrc** button in the Now Playing control bar — one-click export of fetched timed lyrics
- Right-click any lyric line → "Save Lyrics as .lrc"
- **Auto-save fetched lyrics** (Settings → Playback): silently writes a `.lrc` file next to the track when timed lyrics are fetched online, skipping files that already have one; off by default

#### GUI — Performance Presets
- **CPU presets now scale to your hardware** — Low (25%), Medium (50%), High (75%), and Maximum (100%) dynamically use percentages of your logical processor count instead of fixed thread counts
- **Memory presets now scale to your RAM** — High (25% RAM), Very High (50% RAM), and Maximum (75% RAM) dynamically calculate limits based on your total system memory
- Fixed preset labels now display the actual calculated thread count / MB in parentheses

#### GUI — Now Playing Color Cache
- **Cache album art colors** (Settings → Cache & Files → Now Playing Colors) is **on by default** — extracted album-art colors are kept in memory while the app is open so skipping tracks and scrolling through Now Playing with **Color Match** enabled is noticeably smoother. Cache is cleared when the program closes
- **Persist color cache to disk** sub-option (off by default) keeps a tiny amount of color data (a few bytes of RGB per cached track, hashed keys — no file paths) in `%APPDATA%\AudioAuditor\` so the smoothness survives app restarts

#### Scan Stability / Performance
- Fixed GUI and CLI scans that could appear stuck or stop progressing during large analysis runs.
- Analysis now avoids nested blocking worker tasks and batches GUI progress updates to keep scans moving.
- Settings toggles now stay in sync with the DataGrid and the analyzer flags they control.
- Fast defaults keep BPM, DR, True Peak, LUFS, Silence, Rip Quality, Favorites, and Date Created off/hidden unless enabled.
- Malformed metadata no longer prevents otherwise-decodable audio from being analyzed.

#### GUI — Now Playing Polish
- **Tray context menu** was fixed to more match the ColorMatch option when enabled
- **Queue button** added a queue button that syncs with the main window with the same Move Up/Down/Remove/Clear controls, plus an **Up Next** preview
- **Fullscreen/windowed layout presets** now save separately, so custom cover/text/lyrics/visualizer sizes and offsets persist cleanly for each Now Playing mode
- Fixed a corrupted column-visibility state that could collapse the main DataGrid down to only the AI column; the grid now repairs unusable saved/session layouts automatically
- Fixed karaoke apostrophe rendering ("it's" no longer shows as "it s")
- Fixed crash when clicking a lyric line to seek
- Fixed visualizer style dropdown highlight getting out of sync between main window and NP panel
- Fixed **multi-select "Add to Queue"** — now adds all selected files instead of just the first one

#### GUI — Loop Modes
- **Loop Off / Loop All / Loop One** cycle button added to the main playbar and the Now Playing panel
- Loop state persists across sessions; gapless track switching fully respects loop mode

#### GUI — Seek Tooltip
- Hovering over the main seek slider or the NP seek bar shows a time-preview  that follows the cursor

#### GUI — Right-Click Context Menu Flyout
- Reorganized into logical submenus: **Favorites**, **Metadata**, **Analyze / Identify**, **Spectrogram**, **File Operations** — flyouts expand inline to the right, reducing menu height for large file lists

#### GUI — File Operations
- **Rename: Add [Bitrate]** / **Rename: Add [Real Bitrate]** — appends the reported or analyzed bitrate to selected file path(s) to a `.m3u` file (creates if missing)

#### GUI — Analysis Settings
- **Silence gap threshold** — minimum silence duration to flag (default 500 ms), with enable/disable toggle
- **Edge skip zone** — suppress silence flags in the first/last N seconds to avoid false positives on intros/outros
- **Always Full Analysis** — force a complete sample pass even when individual detectors are disabled
- **Frequency Cutoff Allow** — files whose cutoff meets or exceeds a configurable Hz threshold (default 19,600 Hz, off by default) are not flagged as low-quality upconverts
- **RIP Quality** now displays in the Now Playing panel specs when enabled and available

#### GUI — Settings Reorganization
- Settings is now organized into 7 tabs: **Appearance**, **Playback**, **Analysis**, **Cache & Files**, **Export**, **Integrations**, **About**
- **Quick Rename Patterns** (Cache & Files): choose from 3 filename patterns
- **Default Folders** (Cache & Files): set default Copy, Move, and Playlist destinations
- Settings header title is now centered and slightly higher

#### GUI — Playbar Themes
- New **"Follow Theme"** playbar theme option makes the playbar/visualizer colors automatically match the selected app color theme

#### GUI — Abstract Visualizer Removed
- The Abstract (Wave Lines) visualizer has been **removed** for being too glitchy/unreliable — it may return in a future update
- Visualizer style count reduced from 7 to **6** (Bars, Mirror, Particles, Circles, Scope, VU Meter)
- If you have a suggestion for what it should look like if it is to return, send suggestions or suggestions for other new visualizer types are always welcome

#### GUI — AI Detection: Three-State Verdict
- AI column now shows **Yes / Possible / No** (thresholds: ≥70% Yes, 35–70% Possible, <35% No) derived from all enabled detectors; confidence percentage shown below the verdict
- Row highlighting: Possible = amber, Yes = orange/red, No = neutral

#### CLI — Interactive Scan Control
- Press **`p`** during a scan to pause/resume — shows `[FINISHING IN-FLIGHT...]` while workers drain, then `[PAUSED]`; press **`s`** to stop early with `[STOPPING…]` keeping the progress line live until all threads complete (`q` still works for backward compatibility)
- ETA is **off by default**; pass `--eta` to enable it
- Progress updates in place (ANSI cursor positioning); non-ANSI terminals fall back to `\r` overwrite
- Cursor is now hidden during scans to reduce visual clutter
- Completion message printed once; early-stop shows "Scan stopped. X of Y files processed."

#### CLI — Fun Animations
- A mix of star/star like symbols that rotate, rotating scanning phrases (40 entries), rotating tips (16, suppressed with `--no-tips`), and 10 witty completion messages

#### CLI — Config File Support
- `%APPDATA%\AudioAuditor\config` — persistent default flags loaded on every run
- Interactive `config` command launches a guided setup wizard
- `--no-config` skips the config file for a single run

#### CLI — Stdin Pipe Support
- `analyze` and `export` accept file/directory paths piped via stdin (e.g. `echo "D:\Music" | audioauditor analyze`); capped at 50,000 paths

#### CLI — Batch Metadata + Dry Run
- `metadata set` now accepts a directory path to batch-edit every audio file in the folder
- `--dry-run` previews all metadata changes without writing them

#### CLI — AI Detection Parity
- `analyze`, `export`, `info`, and JSON output include the three-state verdict and confidence score
- `info <file>` leads with `AI Detection: {Verdict} ({Confidence}% confidence)`
- JSON adds `aiVerdict` and `aiConfidence` fields; summary counts Yes / Possible / No separately

---

### Fixes

#### Audio — Playback Start Volume Spike
- Extended start-of-play fade-in from 60 ms → 150 ms for a smoother beginning on all formats
- Fixed gapless track switch: removed the `ApplyVolume()` call that briefly set full volume before the fade timer zeroed it, causing a loud pop between gapless tracks
- Added resume-from-pause fade guard: a quick 80 ms fade corrects any volume deviation on resume

#### Audio — Silence Edge-Skip Detection
- Fixed the leading-edge suppression in `RunFullFilePass` using a relative position instead of absolute file position — if a track had 6 seconds of intro silence and a gap appeared at 10 seconds, `silRunStart` was measured from the first audio (4 s) and incorrectly fell inside the 5-second edge zone, suppressing the gap
- Removed a broken trailing-edge check in `DetectSilence` that used the running total of frames read so far (`totalFrames`) rather than the actual file length; after the first edge-skip duration this evaluated true for nearly every frame, causing all mid-track gaps to be silently discarded when edge-skip was enabled

#### GUI — Gapless Now Playing Desync
- `Player_GaplessTrackChanged` now calls `NpSetTrack` so the Now Playing panel updates title, artist, specs, and lyrics on seamless track switches

#### GUI — Now Playing Performance
- Album cover loading and color extraction offloaded to background threads — switching to/from Now Playing no longer freezes the UI on large art
- Main-window album cover updates also offloaded — selecting a new track is noticeably snappier

#### GUI — Lyrics Highlight Reliability
- Dispatch priority changed from `Loaded` → `Render` for faster sync after lyrics load
- One-time catch-up flag on the NP update timer forces an immediate highlight re-evaluation after any track change or lyrics fetch, preventing the "found but not highlighting" stall

#### GUI — Skip Robustness
- Skip-to-next now shows the failing filename and full path instead of a context-free "format not supported"
- `AudioPlayer` resets EQ/spatial/crossfade state after a failed load so subsequent tracks play normally
- Queue auto-advances past a bad file; a consecutive-failure counter (max 3) prevents infinite skip loops

#### GUI — Scan: Wrong File Count with .m3u in Folder
- Dropping a folder containing `.m3u` playlist files no longer doubles the file count — playlists inside folders are not expanded (audio files are already collected directly); dropping a `.m3u` file directly still expands correctly
- Added intra-batch deduplication so the same path can never appear twice in one scan

#### GUI — Scan: Files Dropped When SH Labs AI Is Enabled
- Fixed a bug where stopping a scan (or an SH Labs API timeout) caused entire files to disappear from results instead of just omitting the AI result — all completed audio analysis is now retained

#### GUI — Magma Colormap on Live Spectrogram
- Fixed the Scientific / Magma perceptual gradient not applying to the live spectrogram display — it was only passed to saved exports

#### GUI — Clipping Column Width
- Default width reduced from 250 → 120 px; "No" and "Clipping at 0:30" both fit comfortably

#### Lyrics — Provider Fallback
- All lyric provider failures now log to `System.Diagnostics.Debug` with provider name and exception details
- Auto mode now falls back **LrcLib → Netease → Musixmatch** (previously stopped at Netease)

#### Audio Format Support Cleanup
- Removed `.shn`, `.ra` / `.ram`, and `.caf` from the supported extension list — no Windows Media Foundation decoder exists on standard Windows 10/11; these formats were silently failing to analyze

#### CLI — Export Status Filter
- `export --status` now correctly filters output rows (was a no-op previously)

#### GUI — Scan Progress Reliability
- Fixed scan progress occasionally appearing "stuck" at 99% — wrapped batch processing in `try/finally` to guarantee `_activeBatches` decrement even when an exception occurs
- Removed `ThreadPriority.BelowNormal` from analysis worker threads — restores full scan throughput without CPU throttling

#### GUI — Spectrogram Compare Crash
- Fixed `SpectrogramCompareWindow` crash when opening comparison — it was attempting to decode audio files as bitmap images; now correctly generates spectrograms on background threads via `SpectrogramGenerator.Generate()`

#### GUI — Spectrogram Export Black Padding
- Fixed exported spectrogram PNGs having massive empty black space to the right for short audio files — `SpectrogramGenerator.GenerateRawPixels` was always returning `columns = requestedWidth`, but the sequential `ISampleProvider` cannot seek backward for FFT overlap. When the natural hop size fell below the FFT window size, the reader ran out of samples after `rangeFrames / fftSize` columns and all remaining columns were filled with silence (-200 dB). Now the generator caps `columns` to what the audio actually supports and falls back to non-overlapping windows when overlap isn't possible, producing a fully-filled spectrogram with no black padding

#### GUI — Lyric Menu Polish
- Added checkmark glyph to the lyric save context menu so the active save mode is visually indicated

#### GUI — Guided Tour Removed
- The first-time guided tour and layout hint have been removed to reduce onboarding friction

#### GUI / CLI — Scan Performance
- Fixed severe scan stalling caused by a blocking `GC.Collect(2)` + 10-second sleep loop that triggered on every file when memory usage exceeded the 25 % default limit. Replaced with a lightweight gen-0 hint.

#### GUI — Volume Persistence
- Volume level is now saved across sessions.

#### GUI — Color Match Stability
- Fixed color-match resetting to the default theme when skipping tracks or switching between Now Playing and the main window.

#### GUI — Lyric Seek Crash
- Fixed crash when clicking a lyric line to seek during active scanning.

---

### Performance

- Spectral analysis segments reduced from 200 → 100: roughly halves seek/decode cycles in the spectral pass (~30–40% of per-file time) while retaining accurate frequency cutoff detection
- Analysis tasks dispatched in chunks of 500 instead of one giant batch — prevents thousands of simultaneous tasks when scanning large folders

---

### Security

- **Batch Rename — path traversal**: sanitized path inputs and verified targets stay within the intended directory
- **Archive extraction — ZIP slip**: validated extracted entry paths before writing, with limits on total size and entry count to prevent abuse
- **External process calls**: hardened argument handling to prevent injection via file paths
- **Proxy worker**: fixed a signature verification issue and hardened request sanitization
- **Temp directory entropy**: increased randomness of temporary directory names
- **SMTC cover temp file**: temp cover art now uses randomized filenames per session
- **External program launches**: hardened process startup to prevent argument injection

---

### Integrity Hardening

- Added runtime tamper-detection checks that verify expected assembly structure
- Diagnostics are logged locally only and never shown to users or block startup

---

### Audio Format Support

- **TTA** (True Audio) — decoded via Windows Media Foundation
- **MPC / MP+** (Musepack) — decoded via Windows Media Foundation (requires codec)
- **SPX** (Speex in Ogg) — metadata read via TagLib#
- **MP2** — MPEG Layer II (broadcast/radio)
- **M4B** — MPEG-4 audiobook container
- **M4R** — iPhone ringtone (M4A)
- **MP4** — MPEG-4 audio container
- **3GP / 3G2** — 3GPP/3GPP2 mobile audio
- **AMR** — Adaptive Multi-Rate voice codec
- **AC3** — Dolby AC-3 / Dolby Digital surround
- **MKA** — Matroska audio container
- **WEBM** — WebM audio (Opus/Vorbis)
- **TAK** — Tom's lossless Audio Kompressor

---

## v1.6.0

### New Features

- **Now Playing Panel** — Full immersive Now Playing experience. Click the album cover or press the expand button on the playbar to open a two-column panel: left side shows the album cover with color-matched glows and the song title, right side displays synced lyrics. Background gradient is extracted from the album art for a cohesive look
- **Lyrics System** — Automatic synced lyrics with multiple providers: embedded tags, local `.lrc` files, LrcLib, Netease Music, and Musixmatch. Cycle through providers with the source button. Lyrics auto-scroll and highlight the current line in sync with playback. Click any lyric line to seek to that timestamp. Drag-and-drop `.lrc` files directly onto the lyrics panel to load them
- **Lyrics Translation (beta)** — Translate lyrics to any supported language in real-time. Auto-detects the source language or lets you set it manually
- **Karaoke Mode (beta)** — Word-by-word lyric highlighting that illuminates each word as it's sung, with smooth color transitions
- **Album Color-Match Theming** — Extracts dominant colors from the album cover and applies them to the NP panel background, glows, and visualizer accent colors for a fully themed experience
- **Layout Customization** — Adjust album cover size and position, title size and position, artist/up-next position, lyrics size and position, and visualizer size and position via a popup with live-preview sliders. Position offsets move elements freely without clipping. All preferences persist across sessions
- **Visualizer Placement Options** — Choose between full-width visualizer bar above the playbar or a compact visualizer strip under the album cover
- **Next Track / Artist Preview** — Shows the upcoming track or current artist below the album cover. Click to toggle between artist and up-next display
- **NP Seek Bar** — Dedicated seek slider in the Now Playing panel with proper drag handling — no more position jumping while dragging
- **Integrity Verification** — Built-in checks to verify the application hasn't been tampered with. If AudioAuditor detects modifications to its binaries, it warns users and directs them to the official download. This protects against malware-laced repackages that have been circulating online. Fork-friendly — only activates for builds using the AudioAuditor name

### CLI

- **Interactive Mode** — Launch the CLI with no arguments (or double-click the exe) to enter a persistent REPL session with colored prompts, built-in `cd`/`ls`/`clear` navigation, drag-and-drop path support, and auto-scan on pasted paths
- **Full Analysis Parity** — CLI now supports every analysis feature from the GUI: True Peak (dBTP), integrated LUFS, Dynamic Range, Rip/Encode Quality, SH Labs AI detection, Fake Stereo, Silence Detection, Clipping, MQA, and BPM
- **Analysis Toggle Flags** — Fine-grained control over which checks run: `--no-true-peak`, `--no-lufs`, `--no-dynamic-range`, `--no-clipping`, `--no-mqa`, `--no-silence`, `--no-fake-stereo`, `--no-bpm`, `--fast` (skips DR/TP/LUFS/rip), plus opt-in `--experimental-ai`, `--rip-quality`, `--shlabs`
- **Expanded JSON Output** — `--json` now emits 20+ fields per file including `truePeakDbTP`, `lufsIntegrated`, `dynamicRange`, `ripQuality`, `fakeStereo`, `silenceDetected`, `clipping`, `mqaDetected`, `bpm`, `shLabsConfidence`, `shLabsAiProbability`, `cueSheet`, and more
- **Detailed Info Upgrades** — `info` command output now includes True Peak, LUFS, Rip Quality, SH Labs AI analysis, and Cue Sheet sections
- **Spectrogram Generation** — Generate spectrograms as PNG images for individual files or entire folders via cross-platform SkiaSharp rendering

### Improvements

- **CLI Update Check** — The CLI now properly waits (up to 2 seconds) for the background update check to finish before exiting, so update notifications are no longer silently dropped on fast commands
- **CLI Version Fallback** — Fixed stale hardcoded fallback version string
- **ASCII Art Logo** — Fixed alignment inconsistencies in the startup banner

### Fixes

- **Occlusion Check Timer Leak** — Fixed event handler leak where every window deactivation created a new timer tick handler without removing the old one, causing duplicate handlers to accumulate over time

---

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

### Visualizers

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
- **Light Theme Selected Row Readability** — Fixed selected/playing row text being invisible on the Light theme. DataGrid cells now correctly apply the selection background color so white text is readable
- **Other general fixes & improvements**

---

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
