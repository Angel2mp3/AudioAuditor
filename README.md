<p align="center">
  <img src="Logo.png" alt="AudioAuditor Logo" width="120"/>
</p>

<h1 align="center">AudioAuditor</h1>

<p align="center">
  <b>Audit Your Audio with Confidence</b>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-purple?style=plastic&logo=dotnet" alt=".NET 8"/>
  <img src="https://img.shields.io/badge/Platform-Windows-blue?style=plastic&logo=windows" alt="Windows"/>
  <img src="https://img.shields.io/badge/UI-WPF-0078D4?style=plastic" alt="WPF"/>
  <img src="https://img.shields.io/badge/License-Apache%202.0-blue?style=plastic" alt="License"/>
  <br/>
  <a href="https://ko-fi.com/angelsoftware">
    <img src="https://img.shields.io/badge/Support_on-Ko--fi-FF5E5B?style=plastic&logo=ko-fi&logoColor=white" alt="Support on Ko-fi"/>
  </a>
  <a href="https://audioauditor.org">
    <img src="https://img.shields.io/badge/Website-audioauditor.org-7c5cff?style=plastic&logo=googlechrome&logoColor=white" alt="Website"/>
  </a>
</p>

---

> **🛡️ SECURITY NOTICE — BEWARE OF FAKES**
>
> The **ONLY** official places to download AudioAuditor are:
> - **Website:** [https://audioauditor.org/](https://audioauditor.org/)
> - **GitHub:** [https://github.com/Angel2mp3/AudioAuditor](https://github.com/Angel2mp3/AudioAuditor)
>
> **Any other source claiming to distribute AudioAuditor is a FAKE and almost certainly contains malware.**
> Do not download AudioAuditor from random websites, file-sharing links, or unofficial repositories.
> If you obtained this software from anywhere other than the two links above, **delete it immediately** and download the genuine version.

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
  <img width="1547" height="867" alt="image(31)" src="https://github.com/user-attachments/assets/324bf7c8-4c1a-4d90-999a-053a2bd8975f" />
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
- **Rip/Encode Quality Detection (Experimental)** — Detects bad rips by analyzing zero-sector gaps, clicks/pops, stuck samples, and bit truncation. Opt-in via the feature config overlay
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
- Animated waveform progress bar with smooth edges and multiple playbar themes
- Volume control with mute toggle (click speaker icon)
- Click-to-seek slider with drag support
- Auto-play next track and queue system
- Crossfade support with configurable duration (1–10 seconds)
- Audio normalization toggle (peak-based, targets −1 dB)
- **Hi-res audio support** — Native playback of high sample-rate audio (96 kHz, 192 kHz, etc.) with automatic fallback resampling if the device can't handle the native rate. Optional always-resample mode in Settings (off by default) downsamples >48 kHz to 48 kHz for wider device compatibility
- **Spatial Audio** — Headphone-optimized soundstage widening using crossfeed, HRTF-like interaural time delay, head shadow simulation, and early reflections for a speaker-like experience
- **10-band Parametric Equalizer** — 32 Hz to 16 kHz with ±12 dB per band, soft clipping protection, collapsible panel, and per-band reset
- **Seek Safety Protection** — Multi-layered audio safety system prevents loud pops or static when seeking. Thread-safe audio readers, device-level volume muting during seek, corruption detection, automatic silence buffers, and a hard limiter ensure safe listening at all times

### Now Playing Panel
- **Immersive full-panel view** — Click the album cover or press the expand button on the playbar to open a two-column Now Playing panel: album art with color-matched glows on the left, synced lyrics on the right
- **Album Color-Match Theming** — Dominant colors extracted from the album art are applied to the panel background, glows, and visualizer accent colors for a fully cohesive look
- **Synced Lyrics** — Automatic time-synced lyrics from multiple sources: embedded tags, local `.lrc` files, LrcLib, Netease Music, and Musixmatch. Lyrics auto-scroll and highlight the current line; click any line to seek directly to that timestamp. Cycle through providers with the source button. Drag-and-drop `.lrc` files onto the panel to load them instantly
- **Lyrics Translation (beta)** — Real-time translation into any supported language; auto-detects the source language or lets you set it manually
- **Karaoke Mode (beta)** — Word-by-word highlighting that illuminates each word as it's sung with smooth color transitions
- **Next Track / Artist Preview** — Displays the upcoming track or current artist below the album cover; click to toggle between the two
- **Dedicated Seek Bar** — Full drag-and-seek slider inside the Now Playing panel with no position jumping while dragging
- **Visualizer Placement** — Choose between a full-width visualizer bar above the playbar or a compact strip under the album cover
- **Layout Customization** — Adjust album cover size and position, title and artist text size and position, lyrics panel size and position, and visualizer size and position via a live-preview popup. All layout preferences persist across sessions

### Spectrogram Viewer
- Full-resolution spectrogram generation with logarithmic frequency scaling (20 Hz – Nyquist)
- **Linear frequency scale** — Toggle between logarithmic and linear frequency axis
- **L-R difference channel** — View Left minus Right channel spectrogram to reveal stereo differences; persists across sessions
- **Jump to end** — Zoom into the last 10 seconds of a recording to inspect fade-outs and tail content
- **Mouse wheel zoom** — Scroll to zoom in up to 20x on the spectrogram for detailed inspection; horizontal scrollbar for panning; click the zoom indicator to reset
- Hanning-windowed FFT with 4096-point resolution
- Deep **−130 dB analysis floor** for visibility into low-level content
- Beautiful color gradient: black → blue → purple → red → orange → yellow → white
- Frequency axis labels (50 Hz → 20 kHz)
- Export individual spectrograms as labeled PNG files
- Batch export all spectrograms to a folder
- Double-click spectrogram to save

### Real-time Audio Visualizer
- 64-band FFT frequency visualizer running at 60 FPS
- **7 visualizer modes** — Bars, Mirror, Particles, Circles, Scope, Abstract, VU Meter
- Smooth attack/decay animation with per-mode rendering
- Log-frequency bar distribution matching human hearing
- **Independent visualizer theme** — Choose a separate color theme for the visualizer or follow the playbar
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
- **Metadata Strip Tool** — Remove all metadata tags from selected audio files (ID3, Vorbis, APE, M4A).

### Music Service Integration
- **6 fully configurable slots** — Each toolbar button can be set to any service: Spotify, YouTube Music, Tidal, Qobuz, Amazon Music, Apple Music, Deezer, SoundCloud, Bandcamp, Last.fm, or a fully custom search URL with custom icon
- Click any service button with a track selected to instantly search for it online

### AI Detection (BETA)

AudioAuditor's AI detection tries its best to use **verifiable evidence** - However that being said, these results **can be inacurate**, regardless **please do not use these finding to defame or harrass anyone** :)

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
- **Configurable CPU usage limit** — Choose from Auto (Balanced), Low (2 threads), Medium (4 threads), High (8 threads), or Maximum (16 threads) in Settings
- Auto CPU mode defaults to half your logical processors (clamped 1–16) for a balanced experience
- **Configurable memory limit** — Choose from Auto (Balanced), Low (512 MB), Medium (1 GB), High (2 GB), Very High (4 GB), or Maximum (8 GB)
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

### 11 Animated Playbar Themes

Blue Fire · Neon Pulse · Sunset Glow · Purple Haze · Minimal · Golden Wave · Emerald Wave · Blurple Wave · Crimson Wave · Brown Wave · Rainbow Bars

Each playbar theme has unique gradient colors and animation speed for the waveform visualization. Rainbow Bars cycles through all hues in real-time, including the shuffle and volume button accents.

---

## Getting Started

### Prerequisites
- **Windows 10** or later (x64)
- **No runtime required** — the published executable is fully self-contained with the .NET 8 runtime embedded

### Feature Config Overlay
On first launch (or after a version update), a **Feature Config Overlay** appears letting you enable or disable every analysis feature — Silence Detection, Fake Stereo, Dynamic Range, True Peak, LUFS, Clipping, MQA, AI Detection (default & experimental), and SH Labs API. Disabled features are skipped during analysis and their columns are hidden from the results grid. You can reopen this overlay any time from Settings → Columns & Features.

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
9. **Settings** — Adjust themes, playbar style, play options (crossfade, normalization, spatial audio, rainbow visualizer), music service buttons, EQ, integrations, export format, and performance limits

### Keyboard & Interaction
- **Drag & Drop** — Drop audio files or folders anywhere on the window
- **Column Rearranging** — Drag any DataGrid column header left or right to reorder the layout; the new order is reflected in exports

| Shortcut | Action |
|----------|--------|
| `Space` | Play / Pause |
| `Enter` | Play selected file |
| `Delete` | Remove selected file from list |
| `Ctrl+F` | Focus the search bar |
| `Escape` (in search box) | Clear search and refocus grid |

- **Search Box** — Filter by filename, artist, title, path, extension, or status; use the status dropdown to filter by analysis result
- **Context Menu** — Right-click for Play, Add to Queue, Save Spectrogram, View Album Cover, Open File Location, Copy Path, Copy File Name, Remove
- **Save Album Cover** — Save the original full-quality embedded cover art from the View Album Cover popup, the cover panel next to the spectrogram (right-click), or the metadata editor
- **Double-click spectrogram** — Save as PNG
- **Click volume icon** — Toggle mute

---

## Settings Overview

| Section | Options |
|---------|---------|
| **Appearance** | Color Theme (10 themes), Playbar Style (11 animated themes) |
| **Play Options** | Auto-Play Next, Audio Normalization, Crossfade (1–10s slider), Spatial Audio, Rainbow Visualizer Bars |
| **Music Services** | 6 fully configurable toolbar buttons — pick from 10 preset services or set a custom URL + icon for each |
| **Visualizer** | Mode selection, Auto-Cycle toggle with speed (5–60s), Custom cycle mode list, Independent theme, Full Volume rendering |
| **Columns & Features** | Toggle 25+ analysis columns; disable individual features (Silence, Fake Stereo, DR, True Peak, LUFS, Clipping, MQA, AI) to skip them during analysis and hide their columns |
| **Discord** | Enable/disable Rich Presence, Display mode (track details or file name only) |
| **Last.fm** | API key/secret, browser-based authentication, scrobbling toggle |
| **Export** | Default export format (CSV, TXT, PDF, XLSX, DOCX) |
| **Performance** | CPU usage limit — Auto, Low, Medium, High, Maximum; Memory limit — Auto, Low, Medium, High, Very High, Maximum |

---

## Data & Privacy

AudioAuditor is designed with privacy in mind:

| Data | Storage | Location |
|------|---------|----------|
| Theme preference | `theme.txt` | `%AppData%\AudioAuditor\` |
| Settings & options | `options.txt` | `%AppData%\AudioAuditor\` |
| Last.fm credentials | `session.dat` | `Documents\AudioAuditor\` |
| Analyzed file data | Memory only | Not persisted — cleared on exit |
| Audio queue | Memory only | Not persisted — cleared on exit |
| Spectrograms | Memory only | Only saved if user explicitly exports |

Stored settings include: theme names, boolean flags, service slot names, custom URLs/icons, EQ gains, concurrency/memory limits. No sensitive data in this file. Last.fm session keys are stored separately in your Documents folder.

- **No telemetry or analytics** — zero network calls except when you click a music service search button, use Discord Rich Presence, or scrobble to Last.fm
- **Minimal Disk Usage files and cache** — Only small settings files and temporary archive extractions are written to disk. Analysis data lives in memory and temp files are cleaned up automatically.
- **No logging** — no log files are created
- **Zero AI Training** - nothing analyzed/played is *ever* used to train generative AI

---

## Project Structure

```
AudioAuditor/
├── App.xaml / App.xaml.cs               # Application entry point & theme initialization
├── Audio Quality Checker.sln            # Solution file
├── CHANGELOG.md                         # Version history and release notes
├── Windows/
│   ├── MainWindow.xaml / .xaml.cs       # Main UI — toolbar, DataGrid, player, waveform, visualizer
│   ├── SettingsWindow.xaml / .xaml.cs   # Settings dialog — themes, options, integrations, performance
│   ├── QueueWindow.xaml / .xaml.cs      # Playback queue manager with drag-and-drop reordering
│   ├── ErrorDialog.xaml / .xaml.cs      # Themed error dialog
│   ├── MetadataEditorWindow.xaml / .cs  # Metadata tag editor for audio files
│   ├── MetadataStripWindow.xaml / .cs   # Bulk metadata strip tool
│   ├── BatchRenameWindow.xaml / .cs     # Batch rename & organize using tag patterns
│   ├── DuplicateDetectionWindow.xaml / .cs # Duplicate file detection by metadata/fingerprint
│   └── WaveformCompareWindow.xaml / .cs # Side-by-side waveform comparison with stats
├── Models/
│   └── AudioFileInfo.cs                 # Data model for analyzed files (20+ properties)
├── Converters/
│   └── StatusConverters.cs              # XAML value converters for status, bitrate, clipping, MQA, AI colors
├── Services/
│   ├── AcoustIdService.cs               # AcoustID fingerprinting via fpcalc + MusicBrainz lookup
│   ├── AiWatermarkDetector.cs           # AI audio detection — metadata, byte patterns, C2PA, confidence scoring
│   ├── AudioAnalyzer.cs                 # FFT spectral analysis, quality detection, BPM, replay gain
│   ├── AudioFormatReaders.cs            # Custom format readers for APE, WavPack, DSD, Opus, and ALAC
│   ├── AudioPlayer.cs                   # NAudio playback engine with crossfade, normalization, EQ & spatial pipeline
│   ├── CueSheetParser.cs                # .cue file parser — tracks, indices, timing, relative path resolution
│   ├── DiscordRichPresenceService.cs    # Discord RPC integration
│   ├── Equalizer.cs                     # 10-band parametric EQ (ISampleProvider) with BiQuad filters
│   ├── ExperimentalAiDetector.cs        # Experimental AI detection — spectral/temporal neural watermark analysis
│   ├── ExportService.cs                 # CSV / TXT / PDF / XLSX / DOCX export
│   ├── FlacReader.cs                    # Custom managed FLAC decoder for files NAudio can't handle
│   ├── LastFmService.cs                 # Last.fm scrobbling, Now Playing, and OAuth auth
│   ├── MqaDetector.cs                   # MQA & MQA Studio detection
│   ├── SHLabsDetectionService.cs        # SH Labs cloud AI detection API client
│   ├── SmtcService.cs                   # Windows System Media Transport Controls (SMTC) integration
│   ├── SpatialAudioProcessor.cs         # Spatial audio — crossfeed, HRTF ITD, head shadow, reflections
│   ├── SpectrogramGenerator.cs          # Bitmap spectrogram generation with log-frequency scaling
│   ├── ThemeManager.cs                  # Theme engine, settings persistence, playbar colors
│   └── UpdateChecker.cs                 # GitHub release update checker
├── Resources/
│   ├── icon.png / app.ico               # App icon
│   ├── Spotify.png                      # Service logos
│   ├── YTM.png
│   ├── Tidal.png
│   ├── Qobuz.png
│   ├── Amazon-music.png
│   ├── Apple_music.png
│   ├── Deezer.png
│   ├── Soundcloud.png
│   └── last.fm.png
└── AudioAuditorCLI/
    └── Program.cs                       # CLI entry point — analyze, export, metadata, info commands
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
| [**Concentus**](https://github.com/lostromb/concentus) | 1.0.6 | Managed Opus audio decoding |
| [**TagLibSharp**](https://github.com/mono/taglib-sharp) | 2.3.0 | Audio metadata and tag reading (artist, title, bitrate, sample rate, BPM, Replay Gain, AI detection) |
| [**ClosedXML**](https://github.com/ClosedXML/ClosedXML) | 0.104.2 | Excel XLSX export with styled cells and formatting |
| [**SharpCompress**](https://github.com/adamhathcock/sharpcompress) | 0.38.0 | Archive extraction support |
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
i
---

<p align="center">
  <sub>Built with ❤️ by Angel for audiophiles who care about quality</sub>
</p>
