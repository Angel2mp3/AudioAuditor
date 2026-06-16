<p align="center">
  <img src="Logo.png" alt="AudioAuditor Logo" width="120"/>
</p>

<h1 align="center">AudioAuditor</h1>

<p align="center">
  <b>Audit Your Audio with Confidence</b>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-8366e0?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 8"/>
  <img src="https://img.shields.io/badge/platform-Windows-1fa8fd?style=for-the-badge&logo=windows&logoColor=white" alt="Windows"/>
  <img src="https://img.shields.io/badge/license-Apache%202.0-89276f?style=for-the-badge" alt="Apache 2.0 License"/>
  <img src="https://img.shields.io/badge/version-1.8.0-4f50c6?style=for-the-badge" alt="Version 1.8.0"/>
  <br/>
  <a href="https://audioauditor-download-badge.vercel.app/api/count">
    <img src="https://img.shields.io/endpoint?url=https%3A%2F%2Faudioauditor-download-badge.vercel.app%2Fapi%2Fbadge&style=for-the-badge" alt="Combined downloads"/>
  </a>
  <a href="https://ko-fi.com/angelsoftware">
    <img src="https://img.shields.io/badge/support-Ko--fi-f26b2e?style=for-the-badge&logo=ko-fi&logoColor=white" alt="Support on Ko-fi"/>
  </a>
  <a href="https://audioauditor.org">
    <img src="https://img.shields.io/badge/website-audioauditor.org-4f50c6?style=for-the-badge&logo=googlechrome&logoColor=white" alt="Website"/>
  </a>
  <a href="https://fmhy.net/audio#spectrum-analyzers">
    <img src="https://img.shields.io/badge/featured%20on-FMHY-b051d4?style=for-the-badge" alt="Featured on FMHY"/>
  </a>
</p>

---

> 🛡️ **Official downloads only:** [audioauditor.org](https://audioauditor.org/) or [GitHub](https://github.com/Angel2mp3/AudioAuditor). Any other source is unofficial and may contain malware.

---

## Overview

**AudioAuditor** is a feature-rich audio analysis app for Windows that analyzes your audio files to detect **fake lossless**, verify **true quality**, identify **clipping**, detect **MQA encoding**, detect **AI-generated audio**, estimate **effective frequency cutoffs**, and much more — all wrapped in a sleek, themeable interface with a built-in audio player, equalizer, spatial audio, spectrogram viewer, and real-time visualizer.

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

> **Cross-platform note (CLI):** On Windows every format above is supported. On the **Linux/macOS CLI builds**, decoding uses fully-managed decoders — **FLAC, WAV, AIFF, OGG/Vorbis, Opus, and MP3/MP2** analyze everywhere. Formats that rely on Windows Media Foundation (**AAC/M4A, WMA, ALAC, TTA, Musepack**) still have their metadata read but skip spectral analysis on Linux/macOS. The Windows desktop app and Windows CLI support them all. (This will be worked on in a future version)

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
- **10-band Parametric Equalizer** — 32 Hz to 16 kHz with ±12 dB per band, soft clipping protection, collapsible panel, per-band reset, built-in presets, and custom profile save/delete
- **Seek Safety Protection** — Multi-layered audio safety system prevents loud pops or static when seeking
- **Loop Modes** — Cycle between Loop Off, Loop All, and Loop One with a single button
- **Seek Tooltip** — Hovering over the seek slider shows a live time preview that follows your cursor
- **Favorites** — Star any file to mark it as a favorite; favorites always sort to the top and persist across sessions
- **Main Window Color Match** — Optionally tint the entire UI dynamically from the currently playing track's album art
- **Offline Mode** — Disable all network calls with a single toggle
- **Lyrics Save & Auto-Save** — One-click export of fetched timed lyrics as `.lrc` files, or enable auto-save
- **Mini Player** — A compact, draggable, **always-on-top** floating window (toolbar button) with cover art, title/artist, transport, seek, volume/mute, and shuffle. It has its own optional inline **Circles** visualizer that runs independently of the main window, and it remembers its style, ColorMatch toggle, and size/position across restarts

### Now Playing Panel
- **Immersive full-panel view** — Click the album cover or press the expand button on the playbar to open a two-column Now Playing panel: album art with color-matched glows on the left, synced lyrics on the right
- **Configurable background effects** — Choose Off, Color Drift, Stars, Rain, Snow, **Leaves**, or **Underwater**. Stars include independent per-star twinkle/parallax and occasional shooting stars; Rain has wind-blown streaks with optional lightning; Snow uses soft drifting flakes; Leaves tumble and sway on the wind; Underwater is a calm deep-sea scene with rising bubbles, drifting light shafts, swaying seaweed, and occasional fish. Effect density, lightning frequency, flake/leaf size, and a global animation speed are all adjustable with theme-matched sliders
- **Album Color-Match Theming** — Dominant colors extracted from the album art are applied to the panel background, glows, visualizer accent colors, and even the **Windows title bar** (via DWM) for a fully cohesive look
- **Color Cache** — Cached extracted album-art colors keep skipping and scrolling through Now Playing with **Color Match** enabled smooth and snappy. **On by default**, in-memory only — cleared when the app closes. An optional **Persist color cache to disk** sub-option (off by default) saves a very small amount of color data (a few bytes of RGB per track, with hashed keys — no file paths) to `%APPDATA%\AudioAuditor\` so the smoothness survives app restarts
- **ColorMatch eyedropper** — Pick up to three colors directly from the album cover to override the extracted palette for the current track; right-click the eyedropper to reset
- **Synced Lyrics** — Automatic time-synced lyrics from multiple sources: embedded tags, local `.lrc` files, LrcLib, Netease Music, and Musixmatch. Lyrics auto-scroll and highlight the current line; click any line to seek directly to that timestamp. Cycle through providers with the source button. Drag-and-drop `.lrc` files onto the panel to load them instantly
- **Explicit vs Clean detection** — Reads a version hint from tags (title markers like `(Explicit)`/`[Clean]`/`(Radio Edit)`, ID3v2 `iTunesAdvisory`, MP4 `rtng`) and ranks lyric search candidates so explicit tracks don't get clean lyrics (and vice-versa). A **"Wrong version?"** button in the lyrics header toggles Explicit↔Clean and re-fetches
- **Censored-lyrics auto-fallback** *(opt-in)* — When a provider returns lyrics with profanity masked by `*****`/`#####`, AudioAuditor automatically tries the next provider for a clean copy
- **Look Up This Song** — The Now Playing magnifier has its **own** configurable search-service list (up to 6, with custom URLs/icons), separate from the main window's toolbar buttons; one button seeds it from your main-window setup
- **Focused Lyrics mode** — Keep the active synced lyric line clear while inactive lines are softly blurred for easier reading during playback
- **Lyrics Off Mode** — Hide lyrics completely to show only album art + visualizer
- **Lyrics Translation (beta)** — Real-time translation into any supported language; auto-detects the source language or lets you set it manually
- **Karaoke Mode (beta)** — Word-by-word highlighting that illuminates each word as it's sung with smooth color transitions
- **Next Track / Artist Preview** — Displays the upcoming track or current artist below the album cover; click to toggle between the two
- **Dedicated Seek Bar** — Full drag-and-seek slider inside the Now Playing panel with no position jumping while dragging
- **Visualizer Placement** — Choose between a full-width visualizer bar above the playbar or a compact strip under the album cover
- **Visualizer Drag-Resize** — Grab the handle between the album art and lyrics to resize the visualizer strip from 40–400 px
- **Layout Customization** — Adjust album cover size and position, title and artist text size and position, lyrics panel size and position, and visualizer size and position via a live-preview popup organized into collapsible sections. All layout preferences persist across sessions
- **Layout Profiles** — Save named layouts; profiles are **visualizer-aware**, capturing separate windowed/fullscreen × visualizer-on/off arrangements (sizes, offsets, visualizer height & placement) so one saved look adapts to every mode
- **Album Cover Glow slider** — Size the halo around the cover from off (`0`) through the default soft glow up to a large bloom (`2.0`); the breathing pulse respects the scale and the setting persists
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
- **Auto Rename from metadata** — Right-click selected files and rename them to `Artist - Title` or `Title - Artist` using tag metadata, with collision checks and safe filename cleanup
- **Duplicate Detection** — Scan your library for duplicates by metadata match (artist + title) and file fingerprint (size + duration)
- **Playlist Import** — Import `.m3u`, `.m3u8`, and `.pls` playlist files; resolves relative and absolute paths
- **Cue Sheet Support** — Import `.cue` files; parses track boundaries and adds virtual entries with full analysis
- **Metadata Strip Tool** — Remove all metadata tags from selected audio files (ID3, Vorbis, APE, M4A)
- **Batch Metadata Editor** — Select multiple files and fetch missing tags from online providers (MusicBrainz and others). Pick which fields (title, artist, album, album-artist, year, track/disc, genre, composer, lyrics, cover art…) and providers to use, **preview every proposed change** in a grid, then apply. "Missing-only" by default so existing tags aren't clobbered
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
- **Multi-Service Scrobbling** — Scrobble simultaneously to **Last.fm**, **Libre.fm**, **ListenBrainz**, and **Maloja** (self-hosted). Each is enabled/authenticated independently in Settings → Integrations and a single play fans out to every active service. **Configurable thresholds** (scrobble-at-percent, scrobble-at-seconds, minimum track length — first rule met fires), anti-duplicate by furthest position reached, a **per-song blacklist** (cross-library by Artist|Title), **Pause All Scrobbling**, one-off "Don't Scrobble", and **Scrobble Now**. A corner ♫ status widget in the main window shows state (Scrobbling / Paused / Offline / Not connected) and per-service profile links. Credentials are stored encrypted (Windows DPAPI)
- **Windows Media Session (SMTC)** — Publishes now-playing info to System Media Transport Controls so media overlays (FluentFlyout, volume OSD, etc.) display the current track and album art

### Performance Controls
- **Configurable CPU usage limit** — Choose from Auto (Balanced), Low (25%), Medium (50%), High (75%), or Maximum (100%) in Settings. All presets dynamically scale to your hardware.
- Auto CPU mode defaults to half your logical processors (clamped 1–16) for a balanced experience
- **Configurable memory limit** — Choose from Auto (Balanced), Low (512 MB), Medium (1 GB), High (25% RAM), Very High (50% RAM), or Maximum (75% RAM). All presets dynamically scale to your hardware.
- Auto memory mode defaults to 25% of your total system RAM (clamped 512–8192 MB)
- When memory usage approaches the configured limit, AudioAuditor automatically pauses processing, triggers garbage collection, and waits for memory to free up before continuing
- Both limits apply to file analysis and spectrogram batch export
- Prevents CPU and memory spikes that could lag or freeze your system when processing large folders
- **Reduce Motion** — A single Settings → Appearance toggle that calms the whole app: Now Playing backgrounds, cover glow, lyric transitions, playbar effects, and both the main and mini-player visualizers all stop
- **Battery Saver** — A Settings → Cache & Files performance mode that disables animations to save power, with a master toggle, per-area checkboxes (Now Playing backgrounds, visualizer, cover glow, lyric transitions, waveform & playbar effects), and an "Entire program" option. Applies live, no restart
- **Hardware-acceleration control** — A render-mode selector (Auto / **Force software (CPU only)**) for machines with flaky GPU drivers, plus a read-out of the detected render tier. Applies on restart

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

**Custom Themes** — Build your own theme in Settings → Appearance: name it, set the palette, and watch a live preview update as you drag the controls. Saved custom themes persist and appear alongside the built-ins in the theme picker, and can be re-edited or deleted (built-ins can't).

### Your Wrapped
- **AudioAuditor Wrapped** — A single, roomy stats dashboard of your local listening and library stats: files scanned, hours listened, top artists/albums/tracks, unique albums, favorite formats, library quality, active date range/days active, average plays per track, and your most-listened track by time
- **100% local** — Stats are gathered entirely from your own plays/scans/analyses (opt-in collection), never uploaded, and can be reset anytime
- **Toolbar button** — Opens from a present/gift icon in the main toolbar (between Mini Player and the music-service buttons) and fills the current window instead of forcing fullscreen

### Sessions & Recovery
- **Session Restore** — AudioAuditor remembers which files and folders you had loaded and offers to reload them on the next launch
- **Crash Recovery** — A pending-recovery snapshot lets the app repopulate your working set after an unclean exit; pairs with the scan cache so the re-scan of unchanged files is instant

### Toolbar Customization
- **Optional toolbar buttons** — Settings → Appearance toggles let you hide the **Your Wrapped**, **Mini Player**, and **music-service** buttons if you don't use them (all shown by default)
- **Open With support** — Drag a file/folder onto `AudioAuditor.exe` or use Windows "Open with… → AudioAuditor" to load audio files, archives, playlists, or folders; if the app is already running, the items are forwarded to the existing window instead of being lost

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

### CLI UI: 

When scanning, the CLI isn't just a boring progress bar:

- **🎬 Scanning Word Animation** — Every 9–13 seconds a new word is picked from a rotating vocabulary of 42 terms (*Analyzing, Scrutinizing, Inspecting, Dissecting, Audio-ing, Fingerprinting, Triangulating…*) and smoothly morphs into place letter-by-letter at ~14 letters/second. Suppressed with `--no-fun`.
- **⭐ Pulsing Star** — A Unicode star breathes in and out (`·` → `✦` → `✧` → `★`) next to the progress bar. Changes color to indicate state: **cyan** = running, **yellow** = paused, **red** = stopping.
- **💡 Random Tips** — One of 16 helpful tips appears ~30% of the time at scan start (e.g. *"Tip: Use --fast to skip dynamic range, true peak & LUFS for quicker scans."*). Suppressed with `--no-tips` or `--no-fun`.
- **🎉 Random Completion Messages** — One of 10 witty messages appears ~25% of the time after a scan finishes (e.g. *"All done! Your ears deserve the truth."*). Suppressed with `--no-fun`.
- **⏱️ ETA Display** — Pass `--eta` to see a live estimated time remaining. Calculated from a rolling 30-second completion window with exponential smoothing. Formats as `ETA <10s`, `ETA 45s`, or `ETA 2m 15s`. Default is off to keep the output clean.

**AI Detection Parity** — `analyze`, `export`, `info`, and `--json` output now include the same three-state AI verdict (Yes / Possible / No) and confidence score shown in the GUI. `info <file>` adds a leading `AI Detection: {Verdict} ({Confidence}% confidence)` line above the per-detector breakdown; `--json` adds `aiVerdict` and `aiConfidence` fields.

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
| **Appearance** | Color Theme (10 themes + Custom Theme editor), Playbar Theme (11 + Follow Theme), Visualizer Theme, Rainbow Visualizer Bars, Color Match, Full Volume Visualizer, Reduce Motion, Now Playing visuals & background mode, Now Playing "Look Up This Song" services, optional toolbar buttons |
| **Playback** | Auto-Play Next, Audio Normalization, Crossfade (1–15s), Crossfade on Manual Skip, Gapless Playback, Spatial Audio, Loop Mode, Lyrics Auto-Save |
| **Analysis** | Toggle individual detectors (Silence, Fake Stereo, DR, True Peak, LUFS, Clipping, MQA, AI, BPM, Rip Quality), Silence Threshold, Edge Skip Zone, Frequency Cutoff Allow, Always Full Analysis |
| **Visualizer** | Mode selection (6 modes), Auto-Cycle toggle with speed (5–60s), Custom cycle mode list, Independent theme, Full Volume rendering |
| **Cache & Files** | Scan Cache, Quick Rename Patterns, Default Folders, Focus Newly Added Songs, Session Restore, Battery Saver (master + per-area), Clear Favorites, Clear Cache, Reset Layout |
| **Export** | Default export format (CSV, TXT, PDF, XLSX, DOCX) |
| **Integrations** | Multi-service scrobbling (Last.fm, Libre.fm, ListenBrainz, Maloja) with thresholds, blacklist & pause, Discord Rich Presence, Offline Mode, AcoustID Fingerprinting |
| **Performance** | CPU usage limit — Auto, Low, Medium, High, Maximum; Memory limit — Auto, Low, Medium, High, Very High, Maximum; Hardware acceleration (Auto / Force software) |

---

## Data & Privacy

AudioAuditor is designed with privacy in mind:

| Data | File | Location |
|------|------|----------|
| Theme preference | `theme.txt` | `%AppData%\AudioAuditor\` |
| Settings & options | `options.txt` | `%AppData%\AudioAuditor\` |
| Analysis result cache | `scan_cache.json` | `%AppData%\AudioAuditor\` |
| Favorites | `favorites.json` | `%AppData%\AudioAuditor\` |
| Custom themes | `custom-themes.json` | `%AppData%\AudioAuditor\` |
| EQ profiles | `eq-profiles.json` | `%AppData%\AudioAuditor\` |
| Now Playing layout profiles | `np-layout-profiles.json` | `%AppData%\AudioAuditor\` |
| Album-art color cache *(opt-in disk persist)* | `np_color_cache.json` | `%AppData%\AudioAuditor\` |
| Listening stats *(Wrapped — opt-in)* | `stats.json` | `%AppData%\AudioAuditor\` |
| Session restore & crash recovery | `last_session.json`, `recovery_pending.json` | `%AppData%\AudioAuditor\` |
| Anonymous install ID *(SH Labs opt-in only)* | `install_id.txt` | `%AppData%\AudioAuditor\` |
| Scrobble credentials *(Last.fm / Libre.fm / ListenBrainz / Maloja)* | `session.dat` *(DPAPI-encrypted)* | `Documents\AudioAuditor\` |
| SH Labs result cache *(opt-in only)* | `shlabs_cache.dat` | `Documents\AudioAuditor\` |
| SH Labs rate-limit counters *(opt-in only)* | `shlabs_usage.dat` | `Documents\AudioAuditor\` |
| Analyzed file data | Memory only | Not persisted — cleared on exit |
| Audio queue | Memory only | Not persisted — cleared on exit |
| Spectrograms | Memory only | Only saved if user explicitly exports |

`options.txt` stores theme names, boolean flags, service slot names, custom URLs/icons, EQ gains, scrobble thresholds, the per-song scrobble blacklist, and performance limits — no sensitive data. Scrobble credentials for all four services (Last.fm, Libre.fm, ListenBrainz, Maloja) are stored separately and **encrypted with Windows DPAPI** in `session.dat` in your Documents folder. All three SH Labs files are only created if you opt in to SH Labs detection; the install ID is a random GUID (not derived from any machine info) used solely for rate limiting (15/day, 100/month).

**Network calls** — AudioAuditor makes network requests only in these specific situations:

| Trigger | Destination |
|---------|-------------|
| Click a music service search button | The configured service (Spotify, Tidal, etc.) |
| Discord Rich Presence enabled | Discord IPC (local process only) |
| Scrobbling enabled *(per service)* | Last.fm / Libre.fm / ListenBrainz / your Maloja server |
| SH Labs AI detection *(opt-in)* | Cloudflare Worker proxy — no raw audio leaves your device |
| AcoustID fingerprinting *(user-initiated)* | `api.acoustid.org` + `musicbrainz.org` |
| Update check *(opt-in, silent on startup)* | `api.github.com` — version number only |

- **No telemetry or analytics** — nothing is collected or reported without your explicit action
- **Minimal disk footprint** — only small settings/cache files; temp archive extractions are cleaned up automatically
- **Zero AI training** — nothing analyzed or played is ever used to train generative AI

---

## Project Structure

The codebase is organized into three shipped projects — the **WPF desktop app**, a
platform-independent **`AudioAuditor.Core`** engine, and a cross-platform **CLI** — with large
classes split into focused `partial` files (shown below as `Name(.Aspect/.Aspect).cs`).

```
AudioAuditor/
├── App.xaml / App.xaml.cs                    # WPF entry point — single-instance + GPU/render-mode bootstrap
├── GlobalUsings.cs                           # Shared global using directives
├── AudioQualityChecker.csproj                # WPF desktop app (Windows)
├── Audio Quality Checker.sln                 # Solution
├── CHANGELOG.md  ·  LICENSE (Apache-2.0)
│
├── AudioAuditor.Core/                        # Platform-independent engine — shared by the app and CLI
│   ├── Models/                               # AudioFileInfo, CustomThemeDefinition, NpLayoutProfile
│   ├── Abstractions/                         # Settings interfaces (decouple Core from the WPF app)
│   └── Services/
│       ├── AudioAnalyzer(.Quality/.Loudness/.BpmDetector/.Optimizer/.FullFilePass).cs  # analysis engine (partials)
│       ├── AudioAnalyzerEngine.cs            # FFT / spectral DSP
│       ├── AiWatermarkDetector.cs · ExperimentalAiDetector.cs · SHLabsDetectionService.cs  # AI detection
│       ├── MqaDetector.cs · AcoustIdService.cs · FlacReader.cs · AudioFormatReaders.cs
│       ├── LyricService.cs · MetadataEnrichmentService.cs · ExportService.cs · Equalizer.cs
│       ├── SpatialAudioProcessor.cs · CueSheetParser.cs · ShuffleEngine.cs · SmartRenameService.cs
│       ├── FavoritesService.cs · ScanCacheService.cs · IntegrityVerifier.cs · UpdateChecker.cs
│       └── Scrobbling/                       # IScrobbler + Last.fm / Libre.fm / ListenBrainz / Maloja + ScrobbleManager
│
├── Services/                                 # Desktop-only services (WPF)
│   ├── AudioPlayer(.Crossfade/.Gapless).cs   # NAudio playback (partials)
│   ├── ThemeManager(.Brushes/.NowPlaying/.Persistence/.Scrobbling/.Visualizer/.Performance).cs  # theme engine (partials)
│   ├── AnimationPolicy.cs                     # Reduce Motion / Battery Saver gate
│   ├── SpectrogramGenerator.cs · MiniVisualizerRenderer.cs · SmtcService.cs
│   ├── DiscordRichPresenceService.cs · TranslateService.cs · EqProfileManager.cs
│   └── SessionRestoreService.cs · LocalCrashLogger.cs · LocalStatsCollector.cs · CustomThemeStore.cs
│
├── Windows/                                  # WPF windows & UI (partial-class heavy)
│   ├── MainWindow(.Spectrogram/.Waveform/.MusicServiceSearch/.Overlays/.TitleBar/.Tray/.Wrapped).cs
│   ├── Np*.cs                                # Now Playing — NpCore, NpColors(.Animations/.GlowPulse/.Underwater),
│   │                                         #   NpLyrics, NpEqualizer, NpSearch, NpScrobbleWidget, NpLayout, …
│   ├── SettingsWindow(.Performance/.NowPlaying/.Scrobbling/.NpServices/.CustomThemeEditor).cs
│   ├── NowPlayingWindow · MiniPlayerWindow · CreditsWindow · WelcomeDialog
│   └── Spectrogram / Waveform / Metadata / BatchRename / Duplicate / Queue dialogs
│
├── AudioAuditorCLI/                          # Cross-platform CLI (Windows / Linux / macOS)
│   └── Program(.Commands/.ConsoleUI/.Interactive).cs   # analyze, export, metadata, info, spectrogram, interactive
│
├── Converters/StatusConverters.cs           # XAML value converters (status, bitrate, MQA, AI colors)
├── installer/AudioAuditor.iss               # Inno Setup installer script
├── Third.Party.Notices/                     # Bundled open-source license texts
└── Resources/                               # App icon + service logos
```

---

## Interactive Code Tour

Explore all 137 source files across 10 architectural layers in an interactive graph — click any node to see what it does, or take the guided tour.

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
| [**Last.fm**](https://www.last.fm/api) / [**Libre.fm**](https://libre.fm/) / [**ListenBrainz**](https://listenbrainz.org/) / [**Maloja**](https://github.com/krateng/maloja) | — | Multi-service scrobbling and Now Playing updates |
| [**AcoustID / Chromaprint**](https://acoustid.org/) | — | Audio fingerprinting via fpcalc + MusicBrainz lookup |
| **Windows DWM API** | — | Native title bar color theming via `DwmSetWindowAttribute` |
| **Windows SMTC** | — | System Media Transport Controls for media overlay integration |

---

## Support & Supporters

AudioAuditor is built and maintained in my free time. If you find it useful, consider supporting development so I can keep adding features, improving performance, and squashing bugs.

<a href="https://ko-fi.com/angelsoftware">
  <img src="https://img.shields.io/badge/Support_on-Ko--fi-f26b2e?style=for-the-badge&logo=ko-fi&logoColor=white" alt="Support on Ko-fi"/>
</a>

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
| [**NLayer**](https://github.com/naudio/NLayer) by Mark Heath & Andrew Ward | MIT | Pure-managed MP3/MPEG decoder — enables MP3 analysis on Linux/macOS (and as a Windows fallback) |
| [**Concentus & Concentus.OggFile**](https://github.com/lostromb/concentus) by Logan Stromberg | MIT/BSD | Pure managed Opus audio decoding for .opus file support |
| [**TagLibSharp**](https://github.com/mono/taglib-sharp) by Mono Project | LGPL-2.1 | Reading and writing audio metadata tags across all supported formats (ID3v2, Xiph Comment, APEv2, M4A atoms) |
| [**ClosedXML**](https://github.com/ClosedXML/ClosedXML) by ClosedXML Contributors | MIT | Excel workbook generation with styled cells, headers, and auto-fit columns |
| [**discord-rpc-csharp**](https://github.com/Lachee/discord-rpc-csharp) by Lachee | MIT | Discord Rich Presence client for showing playback status |
| [**SkiaSharp**](https://github.com/mono/SkiaSharp) by Microsoft / Mono | MIT | Spectrogram PNG generation in the CLI |
| [**SharpCompress**](https://github.com/adamhathcock/sharpcompress) by Adam Hathcock | MIT | Archive extraction support (ZIP, RAR, 7Z, TAR) |
| [**System.Security.Cryptography.ProtectedData**](https://github.com/dotnet/runtime) by Microsoft | MIT | Windows DPAPI credential protection for scrobble session storage (Last.fm, Libre.fm, ListenBrainz, Maloja) |

### Framework & Platform

| Technology | By | Usage |
|------------|-----|-------|
| [**.NET 8**](https://github.com/dotnet/runtime) | Microsoft | Application runtime |
| [**WPF**](https://github.com/dotnet/wpf) | Microsoft | UI framework — all windows, controls, data binding, styling, and rendering |

### Algorithms & References

- **MQA Codec Reverse-Engineering** — MQA detection is ported from our own [MQA-Toolkit](https://github.com/Angel2mp3/MQA-Toolkit) Python project. The underlying codec reverse-engineering (the 36-bit sync word and original-sample-rate decoding) is the work of Stavros Avramidis — [**purpl3F0x/MQA_identifier**](https://github.com/purpl3F0x/MQA_identifier) (Apache-2.0) — and [**Dniel97/MQA-identifier-python**](https://github.com/Dniel97/MQA-identifier-python)
- [**Cooley-Tukey FFT Algorithm**](https://en.wikipedia.org/wiki/Cooley%E2%80%93Tukey_FFT_algorithm) — The radix-2 FFT implementation is based on the classic Cooley-Tukey algorithm for spectral analysis
- [**Fisher-Yates Shuffle**](https://en.wikipedia.org/wiki/Fisher%E2%80%93Yates_shuffle) — Modern Fisher-Yates algorithm used for fair deck-based shuffle ensuring every track plays once per cycle
- [**NAudio Documentation & Samples**](https://github.com/naudio/NAudio/tree/master/Docs) — Referenced for `AudioFileReader`, `WaveOutEvent`, `BufferedWaveProvider`, `MixingSampleProvider`, FFT windowing, and `MediaFoundationReader` usage patterns
- [**TagLib# API Reference**](https://github.com/mono/taglib-sharp) — Referenced for multi-format metadata extraction patterns
- [**LAME MP3 Encoder Lowpass Specifications**](https://wiki.hydrogenaud.io/index.php?title=LAME) — Lowpass filter frequency thresholds per bitrate used as reference for bitrate estimation from spectral cutoff detection
- [**Microsoft DWM API Documentation**](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmsetwindowattribute) — Used for `DWMWA_USE_IMMERSIVE_DARK_MODE` and `DWMWA_CAPTION_COLOR` title bar customization
- [**Head-Related Transfer Function (HRTF)**](https://en.wikipedia.org/wiki/Head-related_transfer_function) — Concepts referenced for spatial audio crossfeed, interaural time delay, and head shadow simulation
- [**Last.fm API**](https://www.last.fm/api) / [**Libre.fm**](https://libre.fm/) / [**ListenBrainz**](https://listenbrainz.org/) / [**Maloja**](https://github.com/krateng/maloja) — Scrobbling protocols and authentication flows (Audioscrobbler 2.0 for Last.fm/Libre.fm; ListenBrainz submit API for ListenBrainz/Maloja)
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
