<p align="center">
  <img src="Logo.png" alt="AudioAuditor Logo" width="120"/>
</p>

<h1 align="center">AudioAuditor</h1>

<p align="center">
  <b>Professional audio quality analysis &amp; verification for your music library</b>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-purple?style=flat-square&logo=dotnet" alt=".NET 8"/>
  <img src="https://img.shields.io/badge/Platform-Windows-blue?style=flat-square&logo=windows" alt="Windows"/>
  <img src="https://img.shields.io/badge/UI-WPF-0078D4?style=flat-square" alt="WPF"/>
  <img src="https://img.shields.io/badge/License-MIT-green?style=flat-square" alt="License"/>
</p>

<p align="center">
  <a href="https://ko-fi.com/angelsoftware">
    <img src="https://img.shields.io/badge/Support_on-Ko--fi-FF5E5B?style=for-the-badge&logo=ko-fi&logoColor=white" alt="Support on Ko-fi"/>
  </a>
  &nbsp;
  <a href="https://audioauditor.org">
    <img src="https://img.shields.io/badge/Website-audioauditor.org-7c5cff?style=for-the-badge&logo=googlechrome&logoColor=white" alt="Website"/>
  </a>
</p>

---

## Overview

**AudioAuditor** is a feature-rich desktop application for Windows that analyzes your audio files to detect **fake lossless**, verify **true quality**, identify **clipping**, detect **MQA encoding**, detect **AI-generated audio**, estimate **effective frequency cutoffs**, and much more — all wrapped in a sleek, themeable interface with a built-in audio player, equalizer, spatial audio, spectrogram viewer, and real-time visualizer.

Whether you're an audiophile verifying your FLAC collection, a music producer checking masters, or just curious about the true quality of your library, AudioAuditor gives you the data you need at a glance.

---

<img width="1547" height="867" alt="amethyst-theme" src="https://github.com/user-attachments/assets/1b5b02aa-c9b6-4467-acfa-f61232bd3ddd" />

<img width="1547" height="867" alt="blurple-theme" src="https://github.com/user-attachments/assets/7f3a0792-70a2-46b5-af11-386c2fc32a5b" />



## Features

### Core Analysis
- **Fake Lossless Detection** — Identifies files that claim to be high-quality but are actually upsampled from lower bitrate sources by analyzing spectral content and effective frequency cutoff
- **Spectral Frequency Analysis** — FFT-based spectral analysis (4096-point, Hanning-windowed) determines the true effective frequency ceiling of your audio
- **Clipping Detection** — Digital clipping scan with percentage and sample-count reporting; thorough mode detects clipping even when audio has been scaled down by up to 0.5 dB, reported as "SCALED (dB, %)"
- **MQA Detection** — Identifies MQA and MQA Studio encoded files, reports original sample rate and encoder info
- **AI-Generated Audio Detection** — Scans metadata tags, raw byte patterns, and content provenance markers (C2PA) to identify AI-generated music from 20+ services including Suno, Udio, AIVA, Boomy, and Stable Audio. Features confidence scoring, false-positive filtering against known DAWs/encoders, and AI watermark detection (AudioSeal, SynthID, WavMark)
- **Optimizer Detection** — Detects files that have been processed through audio "optimizers"
- **BPM Detection** — Algorithmic beat detection with tag-based BPM fallback
- **Replay Gain** — Extracts and displays Replay Gain metadata from tags
- **Comprehensive Metadata** — Artist, title, sample rate, bit depth, channels, duration, file size, and bitrate (reported vs. actual)
- **Improved Bitrate Analysis** — Avoids simplistic "320 kbps" labeling for files with steep lowpass filters using a band-energy-drop method; lossless formats (FLAC/WAV/AIFF/APE/WV) report their actual file data rate instead of a lossy-equivalent estimate
- **Custom FLAC Decoder** — Managed FLAC decoder handles files that NAudio cannot decode natively, ensuring full analysis and playback coverage
- **Full Metadata Editor** - Full menu for editing, adding, or removing metadata in an audiofile including search buttons to auto search for the metadata for you

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
- Smooth attack/decay animation
- Log-frequency bar distribution matching human hearing
- Theme-aware accent colors
- **Rainbow mode** — Optional setting where each bar gets its own shifting spectrum color that cycles over time and reacts to amplitude
- Toggle between spectrogram and visualizer modes

### Music Service Integration
- **6 fully configurable slots** — Each toolbar button can be set to any service: Spotify, YouTube Music, Tidal, Qobuz, Amazon Music, Apple Music, Deezer, SoundCloud, Bandcamp, Last.fm, or a fully custom search URL with custom icon
- Click any service button with a track selected to instantly search for it online

### AI Detection (BETA)

AudioAuditor's AI detection tries its best to use **verifiable evidence** - However that being said, these results **can be inacurate**, regardless **please do not use these finding to defame or harrass anyone** :)

| Method | What It Checks |
|--------|---------------|
| **Metadata Tags** | ID3v2, Vorbis, APE, MP4 tags for AI service markers (TXXX frames, comments, encoder fields, free-form atoms) |
| **Raw Byte Patterns** | First 64KB, middle 32KB, and last 64KB of the file for embedded identifiers |
| **C2PA / Content Credentials** | JUMBF box markers, claim manifests, and provenance data |
| **AI Watermarks** | AudioSeal, SynthID, and WavMark watermark identifiers |
| **Confidence Scoring** | Strong markers (named services) score higher than generic phrases; minimum 0.4 threshold required |
| **False-Positive Filtering** | Files produced by known DAWs (Audacity, FL Studio, Ableton, etc.) or encoders (LAME, FFmpeg, etc.) have weak generic markers filtered out |


### Export & Reporting

Five export formats, all matching the current DataGrid column layout:

| Format | Description |
|--------|-------------|
| **Excel (.xlsx)** | Styled workbook with colored status cells, auto-fit columns, frozen header row |
| **CSV (.csv)** | Standard comma-separated values with proper escaping |
| **Text (.txt)** | Formatted report with box-drawing characters, per-file details, and summary statistics |
| **PDF (.pdf)** | Multi-page PDF with monospaced text layout |
| **Word (.docx)** | Minimal OOXML document with bold headers and summary |

All columns exported including: Status, Title, Artist, File Name, File Path, Sample Rate, Bit Depth, Channels, Duration, File Size, Reported Bitrate, Actual Bitrate, Extension, Max Frequency, Clipping, Clipping %, BPM, Replay Gain, MQA, MQA Encoder, AI Detection.

### Queue System
- Dedicated queue window for managing playback order
- Add tracks from the grid via context menu or toolbar button
- Drag-and-drop reordering support
- Auto-advance through the queue

### Integrations
- **Discord Rich Presence** — Shows currently playing track, artist, and time remaining in your Discord status (toggle in Settings)
- **Last.fm Scrobbling** — Full authentication flow with browser-based token exchange, Now Playing updates, and automatic scrobbling at 50% or 4 minutes (whichever comes first)

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

### 10 Animated Playbar Themes

Blue Fire · Neon Pulse · Sunset Glow · Purple Haze · Minimal · Golden Wave · Emerald Wave · Blurple Wave · Crimson Wave · Brown Wave

Each playbar theme has unique gradient colors and animation speed for the waveform visualization.

---

## Getting Started

### Prerequisites
- **Windows 10** or later (x64)
- [**.NET 8.0 Desktop Runtime**](https://dotnet.microsoft.com/download/dotnet/8.0) or SDK

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
- **Ctrl+F** — Focus the search bar
- **Search Box** — Filter by filename, artist, title, path, extension, or status; use the status dropdown to filter by analysis result
- **Context Menu** — Right-click for Play, Add to Queue, Save Spectrogram, View Album Cover, Open File Location, Copy Path, Copy File Name, Remove
- **Save Album Cover** — Save the original full-quality embedded cover art from the View Album Cover popup, the cover panel next to the spectrogram (right-click), or the metadata editor
- **Double-click spectrogram** — Save as PNG
- **Click volume icon** — Toggle mute

---

## Settings Overview

| Section | Options |
|---------|---------|
| **Appearance** | Color Theme (10 themes), Playbar Style (10 animated themes) |
| **Play Options** | Auto-Play Next, Audio Normalization, Crossfade (1–10s slider), Spatial Audio, Rainbow Visualizer Bars |
| **Music Services** | 6 fully configurable toolbar buttons — pick from 10 preset services or set a custom URL + icon for each |
| **Discord** | Enable/disable Rich Presence |
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
├── AudioQualityChecker.csproj           # Main WPF project file
├── Audio Quality Checker.sln            # Solution file
├── Windows/
│   ├── MainWindow.xaml / .xaml.cs       # Main UI — toolbar, DataGrid, player, waveform, visualizer
│   ├── SettingsWindow.xaml / .xaml.cs   # Settings dialog — themes, options, integrations, performance
│   ├── QueueWindow.xaml / .xaml.cs      # Playback queue manager with drag-and-drop reordering
│   ├── ErrorDialog.xaml / .xaml.cs      # Themed error dialog
│   └── MetadataEditorWindow.xaml / .cs  # Metadata tag editor for audio files
├── Models/
│   └── AudioFileInfo.cs                 # Data model for analyzed files (20+ properties)
├── Converters/
│   └── StatusConverters.cs              # XAML value converters for status, bitrate, clipping, MQA, AI colors
├── Services/
│   ├── AiWatermarkDetector.cs           # AI audio detection — metadata, byte patterns, C2PA, confidence scoring
│   ├── AudioAnalyzer.cs                 # FFT spectral analysis, quality detection, BPM, replay gain
│   ├── AudioFormatReaders.cs            # Custom format readers for APE, WavPack, DSD, and ALAC
│   ├── AudioPlayer.cs                   # NAudio playback engine with crossfade, normalization, EQ & spatial pipeline
│   ├── DiscordRichPresenceService.cs    # Discord RPC integration
│   ├── Equalizer.cs                     # 10-band parametric EQ (ISampleProvider) with BiQuad filters
│   ├── ExportService.cs                 # CSV / TXT / PDF / XLSX / DOCX export
│   ├── FlacReader.cs                    # Custom managed FLAC decoder for files NAudio can't handle
│   ├── LastFmService.cs                 # Last.fm scrobbling, Now Playing, and OAuth auth
│   ├── MqaDetector.cs                   # MQA & MQA Studio detection
│   ├── SpatialAudioProcessor.cs         # Spatial audio — crossfeed, HRTF ITD, head shadow, reflections
│   ├── SpectrogramGenerator.cs          # Bitmap spectrogram generation with log-frequency scaling
│   └── ThemeManager.cs                  # Theme engine, settings persistence, playbar colors
├── Resources/
│   ├── icon.png / app.ico               # App icon
│   ├── Spotify.png                      # Service logos
│   ├── YTM.png
│   ├── Tidal.png
│   ├── Qobuz.png
│   ├── Amazon-music.png
│   └── Apple_music.png
└── AudioAuditorCLI/
    ├── AudioAuditorCLI.csproj           # CLI project file (standalone console app)
    └── Program.cs                       # CLI entry point — analyze, export, metadata, info commands
```

---

## Technology

| Technology | Version | Usage |
|------------|---------|-------|
| [**.NET 8**](https://dotnet.microsoft.com/) | 8.0 | Application runtime and SDK |
| [**WPF**](https://github.com/dotnet/wpf) | — | Windows Presentation Foundation UI framework |
| [**NAudio**](https://github.com/naudio/naudio) | 2.2.1 | Audio playback, decoding, FFT analysis, BiQuadFilter EQ, crossfade, sample provider pipeline |
| [**TagLibSharp**](https://github.com/mono/taglib-sharp) | 2.3.0 | Audio metadata and tag reading (artist, title, bitrate, sample rate, BPM, Replay Gain, AI detection) |
| [**ClosedXML**](https://github.com/ClosedXML/ClosedXML) | 0.104.2 | Excel XLSX export with styled cells and formatting |
| [**DiscordRichPresence**](https://github.com/Lachee/discord-rpc-csharp) | 1.2.1.24 | Discord Rich Presence client for playback status |
| [**Last.fm Web API**](https://www.last.fm/api) | — | Scrobbling and Now Playing updates |
| **Windows DWM API** | — | Native title bar color theming via `DwmSetWindowAttribute` |

---

## Contributing

Contributions are welcome! Feel free to open issues or submit pull requests.

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
| [**TagLibSharp**](https://github.com/mono/taglib-sharp) by Mono Project | LGPL-2.1 | Reading and writing audio metadata tags across all supported formats (ID3v2, Xiph Comment, APEv2, MP4 atoms) |
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

This project is licensed under the [MIT License](LICENSE).

---

<p align="center">
  <sub>Built with ❤️ by Angel for audiophiles who care about quality</sub>
</p>



