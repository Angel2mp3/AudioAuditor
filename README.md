<p align="center">
  <img src="Logo.png" alt="AudioAuditor Logo" width="120"/>
</p>

<h1 align="center">AudioAuditor</h1>

<p align="center">
  <b>Audit Your Audio with Confidence</b>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-8366e0?style=plastic&logo=dotnet" alt=".NET 8"/>
  <img src="https://img.shields.io/badge/Platform-Windows-1fa8fd?style=plastic&logo=windows" alt="Windows"/>
  <img src="https://img.shields.io/badge/UI-WPF-55a4f7?style=plastic" alt="WPF"/>
  <img src="https://img.shields.io/badge/License-Apache%202.0-89276f?style=plastic" alt="License"/>
  <img src="https://img.shields.io/badge/Version-1.7.0-4f50c6?style=plastic" alt="Version 1.7.0"/>
  <br/>
  <a href="https://ko-fi.com/angelsoftware">
    <img src="https://img.shields.io/badge/Support_on-Ko--fi-f26b2e?style=plastic&logo=ko-fi&logoColor=white" alt="Support on Ko-fi"/>
  </a>
  <a href="https://audioauditor.org">
    <img src="https://img.shields.io/badge/Website-audioauditor.org-4f50c6?style=plastic&logo=googlechrome&logoColor=white" alt="Website"/>
  </a>
  <a href="https://fmhy.net/audio#spectrum-analyzers">
    <img src="https://img.shields.io/badge/Featured%20on-FMHY-b051d4?style=plastic" alt="Featured on FMHY"/>
  </a>
</p>

---

> 🛡️ **Official downloads only:** [audioauditor.org](https://audioauditor.org/) or [GitHub](https://github.com/Angel2mp3/AudioAuditor). Any other source is unofficial and may contain malware.

---

## Overview

**AudioAuditor** is a feature-rich desktop application for Windows that analyzes your audio files to detect **fake lossless**, verify **true quality**, identify **clipping**, detect **MQA encoding**, detect **AI-generated audio**, estimate **effective frequency cutoffs**, and much more — all wrapped in a sleek, themeable interface with a built-in audio player, equalizer, spatial audio, spectrogram viewer, and real-time visualizer.

Whether you're an audiophile verifying your FLAC collection, a music producer checking masters, or just curious about the true quality of your library, AudioAuditor gives you the data you need at a glance.

---

## Screenshots

<p align="center">
  <img width="1547" height="867" alt="Blurple theme" src="https://github.com/user-attachments/assets/2c4f27df-1ba0-4479-a89d-362502d80d6d" />
  <br/>
  <img width="1547" height="867" alt="Amethyst theme" src="https://github.com/user-attachments/assets/bf064074-c27c-4e58-afb9-74a7dd9842dc" />
</p>

---

## Features

### Core Analysis
- **Fake Lossless Detection** — Identifies files that claim to be high-quality but are actually upsampled from lower bitrate sources by analyzing spectral content and effective frequency cutoff
- **Spectral Frequency Analysis** — FFT-based spectral analysis (4096-point, Hanning-windowed) determines the true effective frequency ceiling of your audio
- **Clipping Detection** — Digital clipping scan with percentage and sample-count reporting; thorough mode detects clipping even when audio has been scaled down by up to 0.5 dB, reported as "SCALED (dB, %)"
- **MQA Detection** — Identifies MQA and MQA Studio encoded files, reports original sample rate and encoder info
- **AI-Generated Audio Detection** — Scans metadata tags, raw byte patterns, and content provenance markers (C2PA) to identify AI-generated music from 20+ services including Suno, Udio, AIVA, Boomy, and Stable Audio. Features confidence scoring, false-positive filtering against known DAWs/encoders, AI watermark detection (AudioSeal, SynthID, WavMark), experimental spectral analysis (7 checks), and SH Labs API integration. The AI column reflects results from **all** enabled detection sources — standard, experimental, and SH Labs
- **Optimizer Detection** — Detects files that have been processed through audio "optimizers"
- **BPM Detection** — Algorithmic beat detection with tag-based BPM fallback
- **Replay Gain** — Extracts and displays Replay Gain metadata from tags
- **Comprehensive Metadata** — Artist, title, sample rate, bit depth, channels, duration, file size, and bitrate (reported vs. actual)
- **Fake Stereo Detection** — Detects mono-duplicated or artificially widened stereo files incorrectly labeled as stereo
- **True Peak Measurement** — Inter-sample true peak level (dBTP) using 4× oversampling, displayed in a dedicated column
- **LUFS Measurement** — Integrated loudness (LUFS / LKFS) per ITU-R BS.1770 with K-weighting
- **Rip/Encode Quality Detection** — Detects bad rips by analyzing zero-sector gaps, clicks/pops, stuck samples, and bit truncation. Opt-in via Settings → Analysis
- **Silence Detection** — Detects excessive silence gaps with configurable threshold (default 500 ms) and edge skip zone to avoid false positives on intros/outros
- **Frequency Cutoff Allow** — Files whose cutoff meets or exceeds a configurable Hz threshold (default 19,600 Hz) are not flagged as low-quality upconverts
- **Always Full Analysis** — Force a complete sample pass even when individual detectors are disabled
- **AcoustID Fingerprinting** — Identify unknown tracks via audio fingerprint against the AcoustID/MusicBrainz database; auto-downloads fpcalc
- **Improved Bitrate Analysis** — Avoids simplistic "320 kbps" labeling for files with steep lowpass filters using a band-energy-drop method; lossless formats (FLAC/WAV/AIFF/APE/WV) report their actual file data rate instead of a lossy-equivalent estimate
- **Custom FLAC Decoder** — Managed FLAC decoder handles files that NAudio cannot decode natively, ensuring full analysis and playback coverage
- **Full Metadata Editor** - Full menu for editing, adding, or removing metadata in an audiofile including search buttons to auto search for the metadata for you
- **Update Checker** — Optionally silently checks for updates in the background each time the program starts

### Supported Formats

| Lossless | Lossy | Other |
|----------|-------|-------|
| FLAC | MP3 | DSF (DSD) |
| WAV | AAC | DFF (DSD) |
| AIFF / AIF | OGG | |
| APE | OPUS | |
| WV (WavPack) | WMA | |
| ALAC | M4A | |

### Built-in Audio Player
- Full playback controls — Shuffle, Previous, Rewind 5s, Play/Pause, Forward 5s, Next
- **Shuffle mode** — Toggle shuffle to play tracks in random order; works with auto-play next, manual next/prev, and queue
- Animated waveform progress bar with smooth edges
- Volume control with mute toggle (click speaker icon)
- Click-to-seek slider with drag support
- Auto-play next track and queue system
- **Crossfade** with configurable duration (1–15 seconds) and **4 curve types** — Equal Power, Linear, Natural, and Sequential (no overlap). Optional **Crossfade on Manual Skip** toggle
- **Gapless Playback** — Seamless track transitions with zero silence gap between consecutive tracks
- Audio normalization toggle (peak-based, targets −1 dB)
- **Hi-res audio support** — Native playback of high sample-rate audio (96 kHz, 192 kHz, etc.) with automatic fallback resampling if the device can't handle the native rate
- **Spatial Audio** — Headphone-optimized soundstage widening using crossfeed, HRTF-like interaural time delay, head shadow simulation, and early reflections
- **10-band Parametric Equalizer** — 32 Hz to 16 kHz with ±12 dB per band, soft clipping protection, collapsible panel, and per-band reset
- **Seek Safety Protection** — Multi-layered audio safety system prevents loud pops or static when seeking
- **Loop Modes** — Cycle between Loop Off, Loop All, and Loop One with a single button
- **Seek Tooltip** — Hovering over the seek slider shows a live time preview that follows your cursor
- **Favorites** — Star any file to mark it as a favorite; favorites always sort to the top and persist across sessions
- **Main Window Color Match** — Optionally tint the entire UI dynamically from the currently playing track's album art
- **Offline Mode** — Disable all network calls with a single toggle
- **Lyrics Save & Auto-Save** — One-click export of fetched timed lyrics as `.lrc` files, or enable auto-save

### Now Playing Panel
- **Immersive full-panel view** — Click the album cover or press the expand button on the playbar to open a two-column Now Playing panel: album art with color-matched glows on the left, synced lyrics on the right
- **Album Color-Match Theming** — Dominant colors extracted from the album art are applied to the panel background, glows, visualizer accent colors, and even the **Windows title bar** (via DWM) for a fully cohesive look
- **Color Cache (v1.7.0)** — Cached extracted album-art colors keep skipping and scrolling through Now Playing with **Color Match** enabled smooth and snappy. **On by default**, in-memory only — cleared when the app closes. An optional **Persist color cache to disk** sub-option (off by default) saves a very small amount of color data (a few bytes of RGB per track, with hashed keys — no file paths) to `%APPDATA%\AudioAuditor\` so the smoothness survives app restarts
- **Synced Lyrics** — Automatic time-synced lyrics from multiple sources: embedded tags, local `.lrc` files, LrcLib, Netease Music, and Musixmatch. Lyrics auto-scroll and highlight the current line; click any line to seek directly to that timestamp. Cycle through providers with the source button. Drag-and-drop `.lrc` files onto the panel to load them instantly
- **Lyrics Off Mode** — Hide lyrics completely to show only album art + visualizer
- **Lyrics Translation (beta)** — Real-time translation into any supported language; auto-detects the source language or lets you set it manually
- **Karaoke Mode (beta)** — Word-by-word highlighting that illuminates each word as it's sung with smooth color transitions
- **Next Track / Artist Preview** — Displays the upcoming track or current artist below the album cover; click to toggle between the two
- **Dedicated Seek Bar** — Full drag-and-seek slider inside the Now Playing panel with no position jumping while dragging
- **Visualizer Placement** — Choose between a full-width visualizer bar above the playbar or a compact strip under the album cover
- **Visualizer Drag-Resize** — Grab the handle between the album art and lyrics to resize the visualizer strip from 40–400 px
- **Layout Customization** — Adjust album cover size and position, title and artist text size and position, lyrics panel size and position, and visualizer size and position via a live-preview popup. All layout preferences persist across sessions
- **Auto-Scaling Layout** — The cover, visualizer, and surrounding layout scale proportionally when the visualizer is toggled on or off, so a single slider setting looks right in both modes without needing separate profiles
- **NP Control Bar** — Shuffle, Loop, Auto-Play, Visualizer, Visualizer Placement, Lyrics Off, Translate, Karaoke, Color Match, Queue, and Settings buttons — all directly accessible from the panel
- **Queue Button & Popup** — Opens the full Queue window with drag-and-drop reordering, plus an **Up Next** preview showing the next track

### Spectrogram Viewer
- Full-resolution spectrogram generation with logarithmic frequency scaling (20 Hz – Nyquist)
- **Linear frequency scale** — Toggle between logarithmic and linear frequency axis
- **L-R difference channel** — View Left minus Right channel spectrogram to reveal stereo differences; persists across sessions
- **Jump to end** — Zoom into the last 10 seconds of a recording to inspect fade-outs and tail content
- **Mouse wheel zoom** — Scroll to zoom in up to 20x on the spectrogram for detailed inspection; horizontal scrollbar for panning; click the zoom indicator to reset
- Hanning-windowed FFT with 4096-point resolution; optional **Hi-Fi mode** uses Blackman-Harris windowing with 16384-point FFT for enhanced detail
- Deep **−130 dB analysis floor** for visibility into low-level content
- Two color gradients: classic (black → blue → purple → red → orange → yellow → white) and **Scientific Magma** perceptual colormap
- Frequency axis labels (50 Hz → 20 kHz)
- **In-memory LRU cache** — Up to 30 spectrograms cached for instant re-opening
- Export individual spectrograms as labeled PNG files
- Batch export all spectrograms to a folder
- Double-click spectrogram to save
- **View Spectrogram** — Right-click any file to open a dedicated fullscreen spectrogram window with Log/Linear, Mono/L-R, Full/End toggles, and a Save button
- **Compare Spectrograms** — Select two files and open a side-by-side full-resolution spectrogram view with:
  - **Stacked** — Top/bottom layout with Fit/Full zoom toggle
  - **Overlay** — Vertical slide-to-merge with a red-channel **diff heatmap** showing pixel-level differences in the overlap region; includes horizontal Offset slider for alignment and Merge slider for blend control
  - **Wipe** — Draggable vertical splitter revealing one file on the left and the other on the right

### Real-time Audio Visualizer
- 64-band FFT frequency visualizer running at 60 FPS
- **6 visualizer modes** — Bars, Mirror, Particles, Circles, Scope, VU Meter
- Smooth attack/decay animation with per-mode rendering
- Log-frequency bar distribution matching human hearing
- **Independent visualizer theme** — Choose a separate color theme for the visualizer, follow the playbar, or set the playbar to **Follow Theme** and match the app color theme automatically
- **Auto-cycle mode** — Automatically rotate between selected visualizer modes on a configurable timer (5–60 seconds)
- **Full volume rendering** — Optional mode that renders the visualizer at full intensity regardless of the current volume level
- Theme-aware accent colors across all modes
- Toggle between spectrogram and visualizer views

### Tools & Batch Operations
- **Waveform Comparison** — Select two files (Ctrl+Click) and compare waveforms side-by-side with correlation, RMS difference, and peak difference stats
- **Batch Rename & Organize** — Rename files using patterns (`{artist}`, `{title}`, `{track}`, etc.) with collision detection and optional folder organization
- **Duplicate Detection** — Scan your library for duplicates by metadata match (artist + title) and file fingerprint (size + duration)
- **Playlist Import** — Import `.m3u`, `.m3u8`, and `.pls` playlist files; resolves relative and absolute paths
- **Cue Sheet Support** — Import `.cue` files; parses track boundaries and adds virtual entries with full analysis
- **Metadata Strip Tool** — Remove all metadata tags from selected audio files (ID3, Vorbis, APE, M4A)
- **Quick Rename** — Right-click → Add `[Bitrate]` or `[Real Bitrate]` to filenames instantly

### Music Service Integration
- **6 fully configurable slots** — Each toolbar button can be set to any service: Spotify, YouTube Music, Tidal, Qobuz, Amazon Music, Apple Music, Deezer, SoundCloud, Bandcamp, Last.fm, or a fully custom search URL with custom icon
- Click any service button with a track selected to instantly search for it online

### AI Detection (BETA)

AudioAuditor's AI detection tries its best to use **verifiable evidence** - However that being said, these results **can be inacurate**, regardless **please do not use these finding to defame or harrass anyone** :)

The AI column shows a **three-state verdict** — **Yes / Possible / No** — derived from the averaged confidence across whichever detectors are enabled (watermark, spectral/experimental, SH Labs). Thresholds: **≥70% → Yes**, **35–70% → Possible**, **<35% → No**. The confidence percentage is shown beneath the verdict, and rows are highlighted accordingly (Possible = amber, Yes = orange/red).

Row highlighting: Possible = amber, Yes = orange/red, No = neutral.

| Method | What It Checks |
|--------|---------------|
| **Metadata Tags** | ID3v2, Vorbis, APE, M4A tags for AI service markers (TXXX frames, comments, encoder fields, free-form atoms) |
| **Raw Byte Patterns** | First 128KB, middle 32KB, and last 128KB of the file for embedded identifiers |
| **C2PA / Content Credentials** | JUMBF box markers, claim manifests, and provenance data |
| **AI Watermarks** | AudioSeal, SynthID, and WavMark watermark identifiers |
| **Confidence Scoring** | Strong markers (named services) score higher than generic phrases; minimum 0.4 threshold required |
| **False-Positive Filtering** | Files produced by known DAWs (Audacity, FL Studio, Ableton, etc.) or encoders (LAME, FFmpeg, etc.) have weak generic markers filtered out |
| **SH Labs API (Opt-in)** | Cloud-based AI speech detection via SH Labs; requires privacy consent and is rate-limited |


### Export & Reporting

Five export formats, all matching the current DataGrid column layout:

| Format | Description |
|--------|-------------|
| **Excel (.xlsx)** | Styled workbook with colored status cells, auto-fit columns, frozen header row |
| **CSV (.csv)** | Standard comma-separated values with proper escaping |
| **Text (.txt)** | Formatted report with box-drawing characters, per-file details, and summary statistics |
| **PDF (.pdf)** | Multi-page PDF with monospaced text layout |
| **Word (.docx)** | Minimal OOXML document with bold headers and summary |

All columns exported including: Status, Title, Artist, File Name, File Path, Sample Rate, Bit Depth, Channels, Duration, File Size, Reported Bitrate, Actual Bitrate, Format, Max Frequency, Clipping, Clipping %, BPM, Replay Gain, Dynamic Range, MQA, MQA Encoder, AI Detection, Fake Stereo, Silence, Date Modified, Date Created, True Peak, LUFS, Rip Quality.

### Queue System
- Dedicated queue window for managing playback order
- Add tracks from the grid via context menu or toolbar button
- Drag-and-drop reordering support
- Auto-advance through the queue

### Integrations
- **Discord Rich Presence** — Shows currently playing track, artist, elapsed time, and song duration bar in your Discord status. Fetches album art from Last.fm when available. Includes play/pause state icons and automatic reconnection (toggle in Settings)
- **Last.fm Scrobbling** — Full authentication flow with browser-based token exchange, Now Playing updates, and automatic scrobbling at 50% or 4 minutes (whichever comes first)
- **Windows Media Session (SMTC)** — Publishes now-playing info to System Media Transport Controls so media overlays (FluentFlyout, volume OSD, etc.) display the current track and album art

### Performance Controls
- **Configurable CPU usage limit** — Choose from Auto (Balanced), Low (25%), Medium (50%), High (75%), or Maximum (100%) in Settings. All presets dynamically scale to your hardware.
- Auto CPU mode defaults to half your logical processors (clamped 1–16) for a balanced experience
- **Configurable memory limit** — Choose from Auto (Balanced), Low (512 MB), Medium (1 GB), High (25% RAM), Very High (50% RAM), or Maximum (75% RAM). All presets dynamically scale to your hardware.
- Auto memory mode defaults to 25% of your total system RAM (clamped 512–8192 MB)
- When memory usage approaches the configured limit, AudioAuditor automatically pauses processing, triggers garbage collection, and waits for memory to free up before continuing
- Both limits apply to file analysis and spectrogram batch export
- Prevents CPU and memory spikes that could lag or freeze your system when processing large folders

### Theming

10 carefully crafted themes with full UI consistency:

| Theme | Description |
|-------|-------------|
| **Dark** | Classic dark mode with subtle grey tones |
| **Ocean** | Deep navy blues inspired by the sea |
| **Light** | Clean light mode with crisp contrast |
| **Amethyst** | Rich purple tones |
| **Dreamsicle** | Warm orange and cream |
| **Goldenrod** | Bright golden yellows |
| **Emerald** | Lush greens |
| **Blurple** | Saturated blue-purple (Discord-inspired) |
| **Crimson** | Bold reds and deep darks |
| **Brown** | Warm chocolate tones |

Each theme covers window backgrounds, panels, toolbars, headers, DataGrid rows (alternating colors and hover states), scrollbars, buttons, inputs, borders, context menus, dropdown menus, title bar caption color (via Windows DWM), and playbar waveform colors.

---

## Getting Started

### Prerequisites
- **Windows 10** or later (x64)
- **No runtime required** — the published executable is fully self-contained with the .NET 8 runtime embedded

### Feature Config Overlay
On first launch (or after a version update), a **Feature Config Overlay** appears letting you enable or disable every analysis feature — Silence Detection, Fake Stereo, Dynamic Range, True Peak, LUFS, Clipping, MQA, AI Detection (default & experimental), and SH Labs API. Disabled features are skipped during analysis and their columns are hidden from the results grid. You can reopen this overlay any time from Settings → Columns & Features.

### CLI

AudioAuditor CLI is a standalone command-line tool with full feature parity to the GUI. It ships as a single self-contained `.exe` — no .NET runtime or dependencies required.

**Interactive Mode** — Launch with no arguments (or double-click the exe) to enter a persistent REPL session with colored prompts, `cd`/`ls`/`clear` navigation, and drag-and-drop path support:

```
audioauditor> scan "D:\Music\album"
audioauditor> info song.flac
audioauditor> export "D:\Music" -o results.csv
```

**Commands:**

| Command | Description |
|---------|-------------|
| `analyze <path>` | Scan files/folders for fake lossless, clipping, MQA, AI, and more |
| `info <file>` | Detailed single-file analysis (True Peak, LUFS, DR, Rip Quality, SH Labs) |
| `export <path> -o <file>` | Analyze and export to CSV, TXT, PDF, XLSX, or DOCX |
| `metadata show\|set\|strip\|remove-cover <file>` | View, edit, strip, or remove cover art from audio metadata tags |
| `spectrogram <path>` | Generate spectrogram PNG images |

**Interactive Mode Commands:**

Launch with no arguments (or double-click the exe) to enter a persistent REPL session:

| Command | Alias | Description |
|---------|-------|-------------|
| `scan <path>` | `analyze` | Scan files or folders for quality |
| `info <file>` | — | Detailed analysis of a single file |
| `export <path> -o <file>` | — | Analyze and export results |
| `metadata <action> <file>` | `meta`, `tags` | View, edit, or strip metadata |
| `spectrogram <path>` | `spectro` | Generate spectrogram PNG(s) |
| `config` | — | Guided config file editor (show / edit / reset / path) |
| `cd [dir]` | — | Change working directory; prints current if no arg |
| `ls` / `dir` | — | List files with color coding (green = audio, yellow = archive, cyan = dirs) |
| `clear` / `cls` | — | Clear terminal screen |
| `version` | — | Show version, runtime, and OS info |
| `help` / `?` | — | Show available commands |
| `exit` / `quit` / `q` | — | Exit interactive mode |

> **Tip:** In interactive mode, you can type or paste any valid file/folder path directly and it will automatically run `analyze` on it.

**Global Flags:**

```
--cpu <mode>        CPU preset: auto, low, medium, high, max (scales to your hardware)
--memory <mb>       Memory limit in MB or preset: auto, low, medium, high, very-high, max
--no-color          Disable colored output (also respects NO_COLOR env variable)
--no-fun            Disable scanning word animations, tips, and completion messages
--eta               Show estimated time remaining during scan (default: off)
--no-update-check   Skip the background update check on startup
--version, -V       Show version, runtime, and OS information
--help, -h          Show usage help
```

**Analysis Flags (`analyze`):**

```
--verbose, -v       Detailed per-file output
--json              Machine-readable JSON output (20+ fields per file)
--fast              Skip DR, True Peak, LUFS, Rip Quality for speed
--experimental-ai   Enable spectral AI detection
--rip-quality       Enable rip/encode quality detection
--shlabs            Enable SH Labs AI detection
--no-clipping       Disable clipping detection
--no-mqa            Disable MQA detection
--no-silence        Disable silence detection
--no-fake-stereo    Disable fake stereo detection
--no-dynamic-range  Disable dynamic range measurement
--no-true-peak      Disable true peak measurement
--no-lufs           Disable LUFS measurement
--no-bpm            Disable BPM detection
--no-tips           Suppress tip messages during analysis
--status <filter>   Show only: real, fake, unknown, corrupt, optimized
--threads <n>       Max parallel threads (default: half logical cores)
--recursive, -r     Recurse into subdirectories (default for folders)
--no-recursive      Do not recurse into subdirectories
--no-config         Skip loading the config file for this run
```

**Export Flags (`export`):**

```
-o, --output <file> Output file path (required)
--format <fmt>      Force export format: csv, txt, pdf, xlsx, docx
--status <s>        Filter results: real, fake, unknown, corrupt, optimized
--threads, --cpu, --memory, --recursive, --no-recursive  Same as analyze
```

**Metadata Flags (`metadata set`):**

```
--title <text>          Set track title
--artist <text>         Set artist
--album <text>          Set album
--album-artist <text>   Set album artist
--year <n>              Set release year
--track <n>             Set track number
--track-count <n>       Set total tracks
--disc <n>              Set disc number
--disc-count <n>        Set total discs
--genre <text>          Set genre
--bpm <n>               Set BPM
--composer <text>       Set composer
--conductor <text>      Set conductor
--grouping <text>       Set grouping
--copyright <text>      Set copyright
--comment <text>        Set comment
--lyrics <text>         Set lyrics
--cover <image-path>    Set album cover from image file (PNG/JPEG/BMP/GIF)
--dry-run               Preview changes without writing (single file or batch)
```

> **Batch metadata editing:** `metadata set <folder>` applies the specified tags to all audio files inside the folder. Respects `--recursive` / `--no-recursive`.

**Spectrogram Flags (`spectrogram`):**

```
--linear              Use linear frequency scale instead of logarithmic
--difference          Render Left–Right channel difference instead of mono
--width <px>          Image width, 200–8000 (default: 1200)
--height <px>         Image height, 100–4000 (default: 400)
--all                 Generate for all files in a folder
```

**Stdin Pipe Support** — Pipe paths directly into the CLI: `echo "D:\Music" | audioauditorCLI analyze` (capped at 50,000 paths). Both `analyze` and `export` accept piped input.

**Config File** — Place default flags in `%APPDATA%\AudioAuditor\cli-config.txt` (one flag per line) and they'll be applied automatically on every run. Run `config` in interactive mode for a guided setup wizard.

**Interactive Scan Controls** — During a scan, press keys without Enter:
- `p` — Toggle pause/resume
- `r` — Resume explicitly
- `q` or `s` — Stop/cancel early

Pause states are shown in the progress bar: `[PAUSED]`, `[FINISHING IN-FLIGHT...]` (while draining workers), or `[STOPPING...]`. Progress uses ANSI cursor positioning to redraw in-place; falls back to `\r` overwrite on legacy terminals.

**Archive Auto-Extraction** — Dropping or passing archive files (`.zip`, `.rar`, `.7z`, `.tar`, `.tgz`) automatically extracts them to a temp folder, scans the contents, and cleans up afterward. Protected against ZIP-slip attacks; capped at 50,000 entries and 5 GB total size.

**Scan Cache** — Results are cached to `scan_cache.json` and reused on subsequent runs if the file size and modification time match, making re-scans of unchanged libraries nearly instant.

---

### CLI Fun Features

When scanning, the CLI isn't just a boring progress bar — it's alive:

- **🎬 Scanning Word Animation** — Every 9–13 seconds a new word is picked from a rotating vocabulary of 42 terms (*Analyzing, Scrutinizing, Inspecting, Dissecting, Audio-ing, Fingerprinting, Triangulating…*) and smoothly morphs into place letter-by-letter at ~14 letters/second. Suppressed with `--no-fun`.
- **⭐ Pulsing Star** — A Unicode star breathes in and out (`·` → `✦` → `✧` → `★`) next to the progress bar. Changes color to indicate state: **cyan** = running, **yellow** = paused, **red** = stopping.
- **💡 Random Tips** — One of 16 helpful tips appears ~30% of the time at scan start (e.g. *"Tip: Use --fast to skip dynamic range, true peak & LUFS for quicker scans."*). Suppressed with `--no-tips` or `--no-fun`.
- **🎉 Random Completion Messages** — One of 10 witty messages appears ~25% of the time after a scan finishes (e.g. *"All done! Your ears deserve the truth."*). Suppressed with `--no-fun`.
- **⏱️ ETA Display** — Pass `--eta` to see a live estimated time remaining. Calculated from a rolling 30-second completion window with exponential smoothing. Formats as `ETA <10s`, `ETA 45s`, or `ETA 2m 15s`. Default is off to keep the output clean.

**AI Detection Parity** — `analyze`, `export`, `info`, and `--json` output now include the same three-state AI verdict (Yes / Possible / No) and confidence score shown in the GUI. `info <file>` adds a leading `AI Detection: {Verdict} ({Confidence}% confidence)` line above the per-detector breakdown; `--json` adds `aiVerdict` and `aiConfidence` fields.

### Install via WinGet

Once the package is available in the [winget-pkgs](https://github.com/microsoft/winget-pkgs) repository, you can install either edition with a single command:

```powershell
# GUI desktop app
winget install Angel.AudioAuditor

# CLI tool
winget install Angel.AudioAuditorCLI
```

WinGet will automatically handle downloading the executable and making it available on your system. The `AudioAuditorCLI` command will be added to your PATH. The GUI app can be launched by running `AudioAuditor` from a terminal or via its shortcut.

> **Note for new versions:** When a new release is published, update the `InstallerUrl`, `InstallerSha256`, and `PackageVersion` fields in the manifests under `winget/manifests/` and submit a PR to [winget-pkgs](https://github.com/microsoft/winget-pkgs).

### Build from Source

```bash
git clone https://github.com/Angel2mp3/AudioAuditor.git
cd AudioAuditor
dotnet build
```

### Run

```bash
dotnet run --project AudioQualityChecker.csproj
```

Or open `Audio Quality Checker.sln` in Visual Studio 2022+ and press **F5**.

---

## Usage

1. **Add Files** — Click **Add Files** or **Add Folder**, or drag & drop audio files/folders directly onto the window
2. **Analyze** — Files are automatically analyzed on import with throttled parallelism; status shows as Real, Fake, Optimized, Unknown, or Corrupt; the status bar displays counts for each category
3. **Filter** — Use the status filter dropdown to show only files with a specific status (Real, Fake, Unknown, Corrupt, Optimized) or search by name/artist/path
4. **Inspect** — Click a file to view its spectrogram and full analysis details in the bottom panel
5. **Play** — Double-click or right-click → Play to start playback with the built-in player
6. **Search** — Click any music service button in the toolbar to search for the selected track online
7. **Export** — Click the **Export ▾** dropdown to save analysis results (CSV, TXT, PDF, XLSX, DOCX) or batch-export spectrograms
8. **Spectrograms** — Right-click → Save Spectrogram to export an individual labeled PNG
9. **Settings** — Adjust themes, play options (crossfade, normalization, spatial audio, rainbow visualizer), music service buttons, EQ, integrations, export format, and performance limits

### Keyboard & Interaction
- **Drag & Drop** — Drop audio files, folders, or archives (`.zip`, `.rar`, `.7z`, `.tar`, `.tgz`) anywhere on the window. Archives are auto-extracted, scanned, and cleaned up
- **Column Rearranging** — Drag any DataGrid column header left or right to reorder the layout; the new order is reflected in exports
- **Column Header Right-Click** — Hide individual columns or show all columns back
- **Auto-Hide Columns** — Columns for disabled features (e.g., AI Detection) automatically hide to reduce clutter
- **Folder Grouping** — Files are grouped by folder with collapsible headers
- **Shift + Scroll** — Horizontal scroll in the file list

| Shortcut | Action |
|----------|--------|
| `Space` | Play / Pause |
| `Enter` | Play selected file |
| `Delete` | Remove selected file from list |
| `Ctrl+F` | Focus the search bar |
| `Escape` (in search box) | Clear search and refocus grid |
| `←` / `→` | Seek backward / forward 5 seconds |
| `↑` / `↓` | Volume up / down |
| `M` | Mute toggle |
| Media Play/Pause | Play / Pause |
| Media Next | Next track |
| Media Previous | Previous track (restart if >3s in, go back if <3s) |
| Media Stop | Stop playback |

- **Search Box** — Filter by filename, artist, title, path, extension, or status; use the status dropdown to filter by analysis result; additional **Mismatched Bitrate** filter shows files where actual bitrate < 80% of reported
- **Context Menu** — Right-click opens 5 submenus:
  - **Favorites** — Star/unstar, Move Up, Move Down
  - **Metadata** — Edit Metadata, Strip Metadata
  - **Analyze / Identify** — AcoustID Fingerprinting
  - **Spectrogram** — View Spectrogram, Compare Spectrograms, Save Spectrogram
  - **File Operations** — Quick Rename (Add `[Bitrate]` / `[Real Bitrate]`), Copy to Folder, Move to Folder, Save to Playlist, Batch Rename, Find Duplicates
- **Save Album Cover** — Save the original full-quality embedded cover art from the View Album Cover popup, the cover panel next to the spectrogram (right-click), or the metadata editor
- **Double-click spectrogram** — Save as PNG
- **Click volume icon** — Toggle mute
- **Scroll-to-Playing** — The file list automatically scrolls to the currently playing track when filters change

### Player Behavior
- **Playback History** — The Previous button first walks back through your playback history before falling back to the list order
- **Restart vs Go-Back** — Pressing Previous when >3 seconds into a track restarts it; pressing it again (or when <3s in) goes to the previous track
- **Consecutive Failure Skip** — If a file fails to load, the player auto-advances past it (max 3 consecutive failures to prevent infinite loops)

### Scan & Analysis
- **Pause/Resume Scanning** — Click the ⏸/▶ button during analysis to pause and resume without losing progress
- **Re-entrant File Adding** — Drop files while a scan is already running and they'll join the current scan batch
- **Archive Drag-Drop** — Drop `.zip`, `.rar`, `.7z`, `.tar`, `.tgz` files directly and they'll be auto-extracted, scanned, and cleaned up

### System Tray
- **Close to Tray** — Closing the window minimizes to the system tray instead of exiting
- **Dark-Themed Tray Menu** — Right-click the tray icon for a dark-themed menu matching the app aesthetic

---

## Settings Overview

| Section | Options |
|---------|---------|
| **Appearance** | Color Theme (10 themes), Playbar Theme (11 + Follow Theme), Visualizer Theme, Rainbow Visualizer Bars, Color Match, Full Volume Visualizer |
| **Playback** | Auto-Play Next, Audio Normalization, Crossfade (1–15s), Crossfade on Manual Skip, Gapless Playback, Spatial Audio, Loop Mode, Lyrics Auto-Save |
| **Analysis** | Toggle individual detectors (Silence, Fake Stereo, DR, True Peak, LUFS, Clipping, MQA, AI, BPM, Rip Quality), Silence Threshold, Edge Skip Zone, Frequency Cutoff Allow, Always Full Analysis |
| **Visualizer** | Mode selection (6 modes), Auto-Cycle toggle with speed (5–60s), Custom cycle mode list, Independent theme, Full Volume rendering |
| **Cache & Files** | Scan Cache, Quick Rename Patterns, Default Folders, Clear Favorites, Clear Cache, Reset Layout |
| **Export** | Default export format (CSV, TXT, PDF, XLSX, DOCX) |
| **Integrations** | Discord Rich Presence, Last.fm Scrobbling, Offline Mode, AcoustID Fingerprinting |
| **Performance** | CPU usage limit — Auto, Low, Medium, High, Maximum; Memory limit — Auto, Low, Medium, High, Very High, Maximum |

---

## Data & Privacy

AudioAuditor is designed with privacy in mind:

| Data | File | Location |
|------|------|----------|
| Theme preference | `theme.txt` | `%AppData%\AudioAuditor\` |
| Settings & options | `options.txt` | `%AppData%\AudioAuditor\` |
| Analysis result cache | `scan_cache.json` | `%AppData%\AudioAuditor\` |
| Anonymous install ID *(SH Labs opt-in only)* | `install_id.txt` | `%AppData%\AudioAuditor\` |
| Last.fm credentials | `session.dat` | `Documents\AudioAuditor\` |
| SH Labs result cache *(opt-in only)* | `shlabs_cache.dat` | `Documents\AudioAuditor\` |
| SH Labs rate-limit counters *(opt-in only)* | `shlabs_usage.dat` | `Documents\AudioAuditor\` |
| Analyzed file data | Memory only | Not persisted — cleared on exit |
| Audio queue | Memory only | Not persisted — cleared on exit |
| Spectrograms | Memory only | Only saved if user explicitly exports |

`options.txt` stores theme names, boolean flags, service slot names, custom URLs/icons, EQ gains, and performance limits — no sensitive data. Last.fm credentials are stored separately in `session.dat` in your Documents folder. All three SH Labs files are only created if you opt in to SH Labs detection; the install ID is a random GUID (not derived from any machine info) used solely for rate limiting (15/day, 100/month).

**Network calls** — AudioAuditor makes network requests only in these specific situations:

| Trigger | Destination |
|---------|-------------|
| Click a music service search button | The configured service (Spotify, Tidal, etc.) |
| Discord Rich Presence enabled | Discord IPC (local process only) |
| Last.fm scrobbling enabled | Last.fm API |
| SH Labs AI detection *(opt-in)* | Cloudflare Worker proxy — no raw audio leaves your device |
| AcoustID fingerprinting *(user-initiated)* | `api.acoustid.org` + `musicbrainz.org` |
| Update check *(opt-in, silent on startup)* | `api.github.com` — version number only |

- **No telemetry or analytics** — nothing is collected or reported without your explicit action
- **Minimal disk footprint** — only small settings/cache files; temp archive extractions are cleaned up automatically
- **No log files** — nothing written to event logs or log files
- **Zero AI training** — nothing analyzed or played is ever used to train generative AI

---

## Project Structure

```
AudioAuditor/
├── App.xaml / App.xaml.cs                      # WPF application entry point & theme bootstrap
├── AudioQualityChecker.csproj                  # WPF GUI project file
├── Audio Quality Checker.sln                   # Solution file
├── CHANGELOG.md                                # Version history and release notes
├── LICENSE                                     # Apache 2.0
│
├── Models/
│   └── AudioFileInfo.cs                        # Core data model — 20+ analysis properties
│
├── Services/                                   # Analysis & integration services
│   ├── AcoustIdService.cs                      # AcoustID fingerprinting + MusicBrainz lookup
│   ├── AiWatermarkDetector.cs                  # AI audio detection — metadata, byte patterns, C2PA
│   ├── AlbumColorExtractor.cs                  # Dominant color extraction from album art
│   ├── AudioAnalyzer.cs                        # Main analysis engine — FFT, BPM, DR, LUFS, True Peak
│   ├── AudioFormatReaders.cs                   # Custom decoders — APE, WavPack, DSD, Opus, ALAC
│   ├── AudioPlayer.cs                          # NAudio playback — crossfade, normalization, EQ, spatial pipeline
│   ├── CueSheetParser.cs                       # .cue file parser with track/index/timing support
│   ├── DiscordRichPresenceService.cs           # Discord Rich Presence integration
│   ├── Equalizer.cs                            # 10-band parametric EQ with BiQuad filters
│   ├── ExperimentalAiDetector.cs               # Spectral/temporal neural watermark analysis
│   ├── ExportService.cs                        # CSV / TXT / PDF / XLSX / DOCX export
│   ├── FlacReader.cs                           # Custom managed FLAC decoder
│   ├── LastFmService.cs                        # Last.fm scrobbling, Now Playing, OAuth
│   ├── LyricService.cs                         # Multi-provider lyrics (LrcLib, Netease, embedded)
│   ├── MqaDetector.cs                          # MQA & MQA Studio detection
│   ├── SHLabsDetectionService.cs               # SH Labs cloud AI detection API client
│   ├── SmtcService.cs                          # Windows SMTC (media key / overlay) integration
│   ├── SpatialAudioProcessor.cs                # Crossfeed, HRTF ITD, head shadow, reflections
│   ├── SpectrogramGenerator.cs                 # Spectrogram bitmap generation with log-frequency scaling
│   ├── ThemeManager.cs                         # Theme engine & settings persistence
│   ├── TranslateService.cs                     # Lyrics translation service
│   └── UpdateChecker.cs                        # GitHub release update checker
│
├── AudioAuditorCLI/
│   ├── AudioAuditorCLI.csproj
│   └── Program.cs                              # Analyze, export, metadata, info, spectrogram, interactive mode
│
├── Converters/
│   └── StatusConverters.cs                     # XAML value converters for status, bitrate, MQA, AI colors
│
├── Windows/
│   ├── MainWindow.xaml / .xaml.cs              # Main UI — toolbar, DataGrid, player, waveform, visualizer
│   ├── NowPlayingWindow.xaml / .xaml.cs        # Immersive Now Playing panel with synced lyrics
│   ├── SettingsWindow.xaml / .xaml.cs          # Settings — themes, options, integrations, performance
│   ├── QueueWindow.xaml / .xaml.cs             # Playback queue with drag-and-drop reordering
│   ├── SpectrogramViewWindow.xaml / .xaml.cs   # Fullscreen single-file spectrogram viewer
│   ├── SpectrogramCompareWindow.xaml / .xaml.cs # Side-by-side spectrogram compare (Stacked/Overlay/Wipe)
│   ├── ErrorDialog.xaml / .xaml.cs             # Error dialog
│   ├── MetadataEditorWindow.xaml / .xaml.cs    # Audio metadata tag editor
│   ├── MetadataStripWindow.xaml / .xaml.cs     # Bulk metadata strip tool
│   ├── BatchRenameWindow.xaml / .xaml.cs       # Batch rename & organize via tag patterns
│   ├── DuplicateDetectionWindow.xaml / .xaml.cs # Duplicate detection by metadata/fingerprint
│   └── WaveformCompareWindow.xaml / .xaml.cs   # Side-by-side waveform comparison with stats
│
└── Resources/
    ├── icon.png / app.ico                      # App icon
    └── [service logos]                         # Spotify, Tidal, Apple Music, YouTube Music, Deezer, etc.
```

---

## Interactive Code Tour

Explore all 23 source files across 6 architectural layers in an interactive graph — click any node to see what it does, or take the guided tour.

<p align="center">
  <a href="https://audioauditor.org/#code">
    <img src="https://img.shields.io/badge/Explore_the_Codebase-Interactive_Code_Tour-4f50c6?style=for-the-badge&logo=googlechrome&logoColor=white" alt="Interactive Code Tour"/>
  </a>
  <br/><br/>
  <img width="1441" height="812" alt="Interactive code tour" src="https://github.com/user-attachments/assets/7a34623f-b58e-4f6a-926b-2185a2ddbf30" />
</p>

---

## Technology

| Technology | Version | Usage |
|------------|---------|-------|
| [**.NET 8**](https://dotnet.microsoft.com/) | 8.0 | Application runtime and SDK |
| [**WPF**](https://github.com/dotnet/wpf) | — | Windows Presentation Foundation UI framework |
| [**NAudio**](https://github.com/naudio/naudio) | 2.2.1 | Audio playback, decoding, FFT analysis, BiQuadFilter EQ, crossfade, sample provider pipeline |
| [**NAudio.Vorbis**](https://github.com/naudio/Vorbis) | 1.5.0 | Ogg Vorbis decoding via NAudio |
| [**Concentus.OggFile**](https://github.com/lostromb/concentus) | 1.0.6 | Managed Opus audio decoding |
| [**TagLibSharp**](https://github.com/mono/taglib-sharp) | 2.3.0 | Audio metadata and tag reading (artist, title, bitrate, sample rate, BPM, Replay Gain, AI detection) |
| [**ClosedXML**](https://github.com/ClosedXML/ClosedXML) | 0.104.2 | Excel XLSX export with styled cells and formatting |
| [**SharpCompress**](https://github.com/adamhathcock/sharpcompress) | 0.38.0 | Archive extraction support |
| [**SkiaSharp**](https://github.com/mono/SkiaSharp) | 2.88.9 | Cross-platform 2D graphics for CLI spectrogram PNG generation |
| [**DiscordRichPresence**](https://github.com/Lachee/discord-rpc-csharp) | 1.2.1.24 | Discord Rich Presence client for playback status |
| [**Last.fm Web API**](https://www.last.fm/api) | — | Scrobbling and Now Playing updates |
| [**AcoustID / Chromaprint**](https://acoustid.org/) | — | Audio fingerprinting via fpcalc + MusicBrainz lookup |
| **Windows DWM API** | — | Native title bar color theming via `DwmSetWindowAttribute` |
| **Windows SMTC** | — | System Media Transport Controls for media overlay integration |

---

## Contributing

AudioAuditor is currently developed by a single maintainer in their free time.
Contributions, suggestions, and feedback are always welcome!

If you'd like to contribute, feel free to open an issue or submit a pull request.

**Getting Started:**
1. Fork the repository
2. Create a feature branch (`git checkout -b feature/my-feature`)
3. Commit your changes (`git commit -am 'Add my feature'`)
4. Push to the branch (`git push origin feature/my-feature`)
5. Open a Pull Request

---

## Credits & Acknowledgments

### Core Libraries

| Library | License | Usage |
|---------|---------|-------|
| [**NAudio**](https://github.com/naudio/naudio) by Mark Heath | MIT | Audio playback, waveform reading, sample provider pipeline, FFT analysis, crossfade mixing, and all audio I/O |
| [**NAudio.Vorbis**](https://github.com/naudio/Vorbis) by Andrew Ward | MIT | OGG Vorbis audio file decoding and playback support |
| [**Concentus & Concentus.OggFile**](https://github.com/lostromb/concentus) by Logan Stromberg | MIT/BSD | Pure managed Opus audio decoding for .opus file support |
| [**TagLibSharp**](https://github.com/mono/taglib-sharp) by Mono Project | LGPL-2.1 | Reading and writing audio metadata tags across all supported formats (ID3v2, Xiph Comment, APEv2, M4A atoms) |
| [**ClosedXML**](https://github.com/ClosedXML/ClosedXML) by ClosedXML Contributors | MIT | Excel workbook generation with styled cells, headers, and auto-fit columns |
| [**discord-rpc-csharp**](https://github.com/Lachee/discord-rpc-csharp) by Lachee | MIT | Discord Rich Presence client for showing playback status |
| [**SkiaSharp**](https://github.com/mono/SkiaSharp) by Microsoft / Mono | MIT | Spectrogram PNG generation in the CLI |
| [**SharpCompress**](https://github.com/adamhathcock/sharpcompress) by Adam Hathcock | MIT | Archive extraction support (ZIP, RAR, 7Z, TAR) |
| [**System.Security.Cryptography.ProtectedData**](https://github.com/dotnet/runtime) by Microsoft | MIT | Windows credential protection for Last.fm session storage |

### Framework & Platform

| Technology | By | Usage |
|------------|-----|-------|
| [**.NET 8**](https://github.com/dotnet/runtime) | Microsoft | Application runtime |
| [**WPF**](https://github.com/dotnet/wpf) | Microsoft | UI framework — all windows, controls, data binding, styling, and rendering |

### Algorithms & References

- [**Cooley-Tukey FFT Algorithm**](https://en.wikipedia.org/wiki/Cooley%E2%80%93Tukey_FFT_algorithm) — The radix-2 FFT implementation is based on the classic Cooley-Tukey algorithm for spectral analysis
- [**Fisher-Yates Shuffle**](https://en.wikipedia.org/wiki/Fisher%E2%80%93Yates_shuffle) — Modern Fisher-Yates algorithm used for fair deck-based shuffle ensuring every track plays once per cycle
- [**NAudio Documentation & Samples**](https://github.com/naudio/NAudio/tree/master/Docs) — Referenced for `AudioFileReader`, `WaveOutEvent`, `BufferedWaveProvider`, `MixingSampleProvider`, FFT windowing, and `MediaFoundationReader` usage patterns
- [**TagLib# API Reference**](https://github.com/mono/taglib-sharp) — Referenced for multi-format metadata extraction patterns
- [**LAME MP3 Encoder Lowpass Specifications**](https://wiki.hydrogenaud.io/index.php?title=LAME) — Lowpass filter frequency thresholds per bitrate used as reference for bitrate estimation from spectral cutoff detection
- [**Microsoft DWM API Documentation**](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmsetwindowattribute) — Used for `DWMWA_USE_IMMERSIVE_DARK_MODE` and `DWMWA_CAPTION_COLOR` title bar customization
- [**Head-Related Transfer Function (HRTF)**](https://en.wikipedia.org/wiki/Head-related_transfer_function) — Concepts referenced for spatial audio crossfeed, interaural time delay, and head shadow simulation
- [**Last.fm API**](https://www.last.fm/api) — Scrobbling protocol and authentication flow
- [**MusicBrainz**](https://musicbrainz.org/), [**Discogs**](https://www.discogs.com/), [**AllMusic**](https://www.allmusic.com/), [**Rate Your Music**](https://rateyourmusic.com/) — Metadata search integration targets

---

## License

This project is licensed under the [Apache License 2.0](LICENSE).

> **Trademark & Brand Notice:**
> The AudioAuditor name, logo, website ([audioauditor.org](https://audioauditor.org)), domain, and all associated brand assets are **not** covered by the Apache 2.0 license and are not part of the open-source grant. They remain the exclusive property of the project owner. You may **not** use the name, logo, or brand assets without explicit written permission.

---

<p align="center">
  <sub>Built with ❤️ by Angel for audiophiles who care about quality</sub>
</p>
