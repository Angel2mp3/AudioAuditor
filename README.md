<a id="top"></a>

<p align="center">
  <img src="Logo.png" alt="AudioAuditor Logo" width="120"/>
</p>

<h1 align="center">AudioAuditor</h1>

<p align="center">
  <b>Audit Your Audio with Confidence</b>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD5?style=round" alt=".NET 8"/>
  <img src="https://img.shields.io/badge/License-Apache%202.0-89276f?style=round" alt="Apache 2.0 License"/>
  <br/>
  <a href="https://github.com/Angel2mp3/AudioAuditor/releases"><img src="https://img.shields.io/github/downloads/Angel2mp3/AudioAuditor/total?style=round&color=4F50C6" alt="Downloads"/></a>
  <a href="https://ko-fi.com/angelsoftware"><img src="https://img.shields.io/badge/Support-Ko--fi-f26b2e?style=round&logo=ko-fi&logoColor=white" alt="Support on Ko-fi"/></a>
  <a href="https://github.com/Angel2mp3/AudioAuditor"><img src="https://img.shields.io/badge/GUI-Windows-0060BF?style=round" alt="GUI: Windows"/></a>
  <a href="https://github.com/Angel2mp3/AudioAuditor/tree/main/AudioAuditorCLI"><img src="https://img.shields.io/badge/CLI-Windows%20%2B%20Linux-1B1C31?style=round" alt="CLI: Windows + Linux"/></a>
</p>

<br/>

<p align="center">
  <b>⭐ Featured & Reviewed By</b>
</p>

<p align="center">
  <a href="https://www.softpedia.com/get/Multimedia/Audio/AudioAuditor.shtml" target="_blank" rel="noopener noreferrer"><img src="https://img.shields.io/badge/Softpedia-5%2F5%20editorial%20review-00205C?style=round" alt="Softpedia — 5/5 Editorial Review"/></a>
  <a href="https://bedroomproducersblog.com/2026/07/28/audioauditor/" target="_blank" rel="noopener noreferrer"><img src="https://img.shields.io/badge/BPB-Featured%20article-33373A?style=round" alt="Bedroom Producers Blog — Featured Article"/></a>
  <a href="https://fmhy.net/audio#spectrum-analyzers" target="_blank" rel="noopener noreferrer"><img src="https://img.shields.io/badge/FMHY-Curated%20listing-6B65AE?style=round" alt="FMHY — Curated Listing"/></a>
</p>

<p align="center">
  <a href="https://trendshift.io/repositories/22767?utm_source=trendshift-badge&utm_medium=badge&utm_campaign=badge-trendshift-22767" target="_blank" rel="noopener noreferrer">
    <img src="assets/trendshift-badge-dark.svg" alt="Angel2mp3/AudioAuditor | Trendshift" width="250" height="55"/>
  </a>
</p>

---

> 🛡️ **Official downloads only:** [audioauditor.org](https://audioauditor.org/) and [GitHub](https://github.com/Angel2mp3/AudioAuditor). Any other source is unofficial and may contain malware.

---

## 📑 Table of Contents

<p align="center">
  <a href="#overview"><kbd>Overview</kbd></a>
  <a href="#screenshots"><kbd>Screenshots</kbd></a>
  <a href="#features"><kbd>Features</kbd></a>
  <a href="#getting-started"><kbd>Getting Started</kbd></a>
  <a href="#usage"><kbd>Usage</kbd></a>
  <a href="#settings-overview"><kbd>Settings</kbd></a>
  <a href="#data--privacy"><kbd>Data & Privacy</kbd></a>
  <a href="#project-structure"><kbd>Project Structure</kbd></a>
  <a href="#interactive-code-tour"><kbd>Code Tour</kbd></a>
  <a href="#technology"><kbd>Technology</kbd></a>
  <a href="#faq"><kbd>FAQ</kbd></a>
  <a href="#support--supporters"><kbd>Support</kbd></a>
  <a href="#contributing"><kbd>Contributing</kbd></a>
  <a href="#credits--acknowledgments"><kbd>Credits</kbd></a>
  <a href="#license"><kbd>License</kbd></a>
</p>

---

## Overview

**AudioAuditor** is a feature-rich audio analysis app for Windows that analyzes your audio files to detect **fake lossless**, verify **true quality**, identify **clipping**, detect **MQA encoding**, detect **AI-generated audio**, estimate **effective frequency cutoffs**, and much more — all wrapped in a sleek, themeable interface with a built-in audio player, equalizer, spatial audio, spectrogram viewer, and real-time visualizer.

Whether you're an audiophile verifying your FLAC collection, a music producer checking masters, or just curious about the true quality of your library, AudioAuditor gives you the data you need at a glance.

---

## Screenshots

<p align="center">
  <img width="1599" height="867" alt="AudioAuditor main window using the blurple theme" src="https://github.com/user-attachments/assets/f3ff6ceb-bea6-483f-8d38-53395e0dbfe0" />
  <br/>
  <img width="1599" height="867" alt="AudioAuditor main window using the amethyst theme" src="https://github.com/user-attachments/assets/ae5a5a01-ebad-453d-9bed-a105de3be576" />
  <img width="1483" height="762" alt="AudioAuditor CLI running a scan" src="https://github.com/user-attachments/assets/4efa884a-0e9e-4ed6-a90e-4505baa33c50" />

</p>

---

## Features

### Core Analysis
- **Fake Lossless Detection** — Identifies files that claim to be high-quality but are actually upsampled from lower bitrate sources by analyzing spectral content and effective frequency cutoff
- **Spectral Frequency Analysis** — FFT-based spectral analysis (4096-point, Hanning-windowed) determines the true effective frequency ceiling of your audio
- **Clipping Detection** — Digital clipping scan with percentage and sample-count reporting; thorough mode detects clipping even when audio has been scaled down by up to 0.5 dB, reported as "SCALED (dB, %)"
- **MQA Detection** — Identifies MQA and MQA Studio encoded files, reports original sample rate and encoder info
- **AI-Generated Audio Detection** — Scans metadata tags, raw byte patterns, and content provenance markers (C2PA) to identify AI-generated music from 20 named services including Suno, Udio, AIVA, Boomy, Stable Audio, Riffusion, ElevenLabs, and Google Lyria. Features confidence scoring weighted by evidence strength, false-positive filtering against known DAWs/encoders, AI watermark detection (AudioSeal, SynthID, WavMark), experimental spectral analysis (7 checks), and SH Labs API integration. The AI column reflects results from **all** enabled detection sources — standard, experimental, and SH Labs — and names which tier of evidence the verdict rests on
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
- **Full Metadata Editor** - Full menu for editing, adding, or removing metadata in an audio file, including search buttons to auto-search for the metadata for you
- **Update Checker** — Optionally silently checks for updates in the background each time the program starts

### Supported Formats

| Lossless | Lossy | Other |
|----------|-------|-------|
| FLAC | MP3 | DSF (DSD) |
| WAV | MP2 | DFF (DSD) |
| AIFF / AIF | AAC | AU / SND |
| APE | OGG | MKA (Matroska) |
| WV (WavPack) | OPUS | WEBM |
| ALAC | WMA | 3GP / 3G2 |
| TTA | M4A / M4B / M4R | AMR |
| TAK | MP4 | AC3 |
| | Musepack (MPC / MP+) | |
| | Speex (SPX) | |

> **Cross-platform note (CLI):** **FLAC, WAV, AIFF, OGG/Vorbis, Opus, MP3/MP2 and DSD** use fully-managed decoders and analyze everywhere with no setup. Every other format in the table is decoded through **ffmpeg**, which AudioAuditor runs as a separate process — so install it to get full coverage on Linux and macOS:
>
> ```
> sudo apt install ffmpeg      # Debian/Ubuntu
> brew install ffmpeg          # macOS
> ```
>
> On **Windows**, grab a build from **<https://ffmpeg.org/download.html>** (pick one whose name contains `lgpl`) and either put it on your `PATH` or drop `ffmpeg.exe` into a `third-party\ffmpeg\` folder next to `AudioAuditor.exe`. The Batch Editor's Convert tab has a **"Get ffmpeg…"** button that opens that same page and an **"Open folder"** button that opens the exact folder to drop it in.
>
> Run `audioauditorcli --version` to confirm it was found. Without ffmpeg, the managed formats still analyze normally and the rest are read for metadata but skipped for quality analysis. On Windows, AAC/M4A, ALAC, WMA and friends also decode through the built-in Media Foundation codecs, so ffmpeg is only needed there for the formats Windows has no codec for (**APE, WavPack, TAK, Musepack, Speex**).

### Built-in Audio Player
- Full playback controls — Shuffle, Previous, Rewind 5s, Play/Pause, Forward 5s, Next
- **Shuffle mode** — Toggle shuffle to play tracks in random order; works with auto-play next, manual next/prev, and queue
- Animated waveform progress bar with smooth edges
- Volume control with mute toggle (click speaker icon)
- Click-to-seek slider with drag support
- Auto-play next track and queue system
- **Crossfade** with configurable duration (1–30 seconds) and **4 curve types** — Equal Power, Linear, Natural, and Sequential (no overlap). Optional **Crossfade on Manual Skip** toggle
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
- **Color Cache** — Cached extracted album-art colors keep skipping and scrolling through Now Playing with **Color Match** enabled smooth and snappy. The cache is always on and always saved to `%APPDATA%\AudioAuditor\`, so the smoothness survives app restarts. It stores a very small amount of color data (a few bytes of RGB per track, with hashed keys — no file paths), and you can clear it any time from Settings → Cache & Files
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

### Batch Editor

The Batch Editor is one window with five tabs, opened from the grid's right-click menu with any number of files selected. Every tab previews its changes before anything is written, and per-file backups are optional throughout.

- **Manual Edit** — Set any tag field (title, artist, album, album artist, year, genre, composer, comment, disc) across the whole selection at once. Only the fields you tick are written, so everything else is left untouched. Includes track-number tools (one fixed number, or auto-numbering in list order) and full cover-art control: pick one image for all, fetch covers online per album, or strip covers entirely. A one-click "Fill missing fields online" searches the metadata providers and applies high-confidence matches to empty fields only.
- **Auto-Tag** — Searches MusicBrainz, iTunes, Deezer, TheAudioDB, AcoustID, and Cover Art Archive on open, with optional Discogs and fanart.tv once you supply a token. Files report success or failure as each one finishes, cover thumbnails stream into the list, and an "unmatched" button explains exactly which files could not be matched and why. Understands soundtracks and compilations, and can embed a streaming link in the comment field.
- **Rename** — The custom-pattern engine with tokens (`{artist}`, `{title}`, `{albumartist}`, `{track3}`, `{disc}`, `{genre}`), case modifiers, configurable track padding, find/replace, and inline-editable proposed names. Whole-name transforms for case, spaces, and stripping `(feat. …)` apply in both manual and smart modes and are remembered between sessions.
- **Convert** — Converts the selection between MP3, FLAC, WAV, AAC/M4A, OGG Vorbis, Opus, WMA, and AIFF using FFmpeg, carrying tags and embedded cover art across where the target format supports them. Per-codec quality and bitrate controls, a choice of output folder or in-place conversion, and overwrite / delete-original options. FFmpeg runs as a separate process and is never linked, which keeps AudioAuditor under Apache-2.0. If no FFmpeg is found the tab explains where to get one and where to put it; everything else keeps working.
- **Clean Up** — Strips download, source, and sponsor junk out of free-text tag fields: "downloaded from …", "ripped using …", bare website names, and promo calls-to-action in the Comment and Title fields. Conservative by design — only the matched span is removed rather than the whole field, a Title is never emptied, and a field with no junk in it is left completely untouched. Optionally wipes the Comment field outright instead. Every proposed change is previewed before anything is written.

### Metadata Tools
- **Paste metadata** — Paste a blob of text and let AudioAuditor work out what goes where. It recognizes a tracklist (numbered lines or `Artist - Title`, fuzzy-matched to your files), a CSV or table (one row per file, matched by a filename column or row order), and a single `Field: value` block applied to every file. You can also copy album-level fields and cover art from one well-tagged master file onto the rest.
- **Transfer metadata between folders** — Copy tags and cover art from one folder of files onto another, even across formats. Point it at a source folder whose files are well tagged and it matches each of your loaded files to its counterpart by title and filename, with a positional fallback when the counts line up. Files with no close enough match are left untouched rather than guessed at.
- **Write analysis to files** — Write AudioAuditor's own measurements back into the audio files: a dedicated custom tag per metric plus a single human-readable summary line in the comment field. Pick exactly which metrics to write. Tags go into the containers a file already carries, so a FLAC does not come back with an ID3 tag stapled to it.
- **Batch Metadata Editor** — Select multiple files and fetch missing tags from online providers (MusicBrainz and others). Pick which fields (title, artist, album, album-artist, year, track/disc, genre, composer, lyrics, cover art…) and providers to use, **preview every proposed change** in a grid, then apply. "Missing-only" by default so existing tags aren't clobbered
- **Metadata Strip Tool** — Remove all metadata tags from selected audio files (ID3, Vorbis, APE, M4A)

### Tools & Batch Operations
- **CD Rip Checker** — Score an EAC, XLD, or whipper rip log with the OPS deduction model, showing the 0–100 score and every deduction. Available three ways: a dedicated **Tools → Check CD Rip Log…** window that accepts a dropped `.log` file, the `checklog` CLI command, and an opt-in **Rip Log** scan column that finds the log sitting next to your files automatically.
- **Sheet music lookup** — Right-click a file (or several) → **Analyze / Identify → Find Sheet Music…**, or use the button on the Now Playing bar, to search IMSLP's public-domain catalog for real matches with real download links. Multi-selections step through one at a time. Anything outside that catalog falls back to a one-click web search.
- **Waveform Comparison** — Select two files (Ctrl+Click) and compare waveforms side-by-side with correlation, RMS difference, and peak difference stats
- **Batch Rename & Organize** — Rename files using patterns (`{artist}`, `{title}`, `{track}`, etc.) with collision detection and optional folder organization
- **Auto Rename from metadata** — Right-click selected files and rename them to `Artist - Title` or `Title - Artist` using tag metadata, with collision checks and safe filename cleanup
- **Duplicate Detection** — Scan your library for duplicates by metadata match (artist + title) and file fingerprint (size + duration)
- **Playlist Import** — Import `.m3u`, `.m3u8`, and `.pls` playlist files; resolves relative and absolute paths
- **Cue Sheet Support** — Import `.cue` files; parses track boundaries and adds virtual entries with full analysis
- **Quick Rename** — Right-click → Add `[Bitrate]` or `[Real Bitrate]` to filenames instantly

### Music Service Integration
- **6 fully configurable slots** — Each toolbar button can be set to any service: Spotify, YouTube Music, Tidal, Qobuz, Amazon Music, Apple Music, Deezer, SoundCloud, Bandcamp, Last.fm, or a fully custom search URL with custom icon
- Click any service button with a track selected to instantly search for it online

### AI Detection (BETA)

AudioAuditor's AI detection tries its best to use **verifiable evidence**. However, these results **can be inaccurate**; please do not use these **findings** to defame or harass anyone. :)

The AI column shows a **three-state verdict** — **Yes / Possible / No** — with the confidence percentage and the tier of evidence it rests on, all on one line: `Yes - watermark (86%)`, `Possible - heuristic (52%)`, or plain `No`. Thresholds: **≥70% → Yes**, **35–70% → Possible**, **<35% → No**.

Row highlighting: Possible = amber, Yes = orange/red, No = neutral.

**How the verdict is reached.** The detectors are not equally trustworthy, so they are not pooled as if they were. Each contributes in proportion to how much it can actually prove, and the results are combined so that independent evidence *reinforces* — adding a weak signal can never drag a strong one down:

| Evidence tier | Weight | Why |
|---|---|---|
| **Watermark / metadata / C2PA** | Full | A named generator tag, an AudioSeal/SynthID watermark, or a C2PA manifest is verifiable evidence, not inference |
| **SH Labs model** | High | A real trained model, but a remote black box |
| **Spectral heuristics** | Half | Proxies for "sounds over-processed" — which heavily limited human masters also trigger |

Two consequences worth knowing: **spectral heuristics on their own can never produce a confident "Yes"** — at most "Possible", because only verifiable evidence or the trained model is allowed to accuse a file outright. And when the SH Labs model has listened to a file and confidently reports it as human, that **overrides a spectral flag** — but never overrides an embedded watermark, because a manifest is a fact and a model result is an opinion.

| Method | What It Checks |
|--------|---------------|
| **Metadata Tags** | ID3v2, Vorbis, APE, M4A tags for AI service markers (TXXX frames, comments, encoder fields, free-form atoms) |
| **Raw Byte Patterns** | First 128KB, middle 32KB, and last 128KB of the file for embedded identifiers |
| **C2PA / Content Credentials** | Content Credentials marker strings (`c2pa.claim`, `c2pa.actions`) found in the scanned regions. Marker matching only — manifests are not decoded and their signatures are not verified |
| **AI Watermarks** | AudioSeal, SynthID, and WavMark watermark identifiers |
| **Confidence Scoring** | Scored by marker *strength*, not count: one strong marker (a named service or a watermark) is enough on its own; generic phrases score far lower and a single one is never reported |
| **False-Positive Filtering** | Files produced by known DAWs (Audacity, FL Studio, Ableton, etc.) or encoders (LAME, FFmpeg, etc.) have weak generic markers filtered out |
| **SH Labs API (Opt-in)** | Cloud-based AI speech detection via SH Labs; requires privacy consent and is rate-limited (15/day, 100/month) |


### Export & Reporting

Five export formats, all matching the current DataGrid column layout:

| Format | Description |
|--------|-------------|
| **Excel (.xlsx)** | Styled workbook with colored status cells, auto-fit columns, frozen header row |
| **CSV (.csv)** | Standard comma-separated values with proper escaping |
| **Text (.txt)** | Formatted report with box-drawing characters, per-file details, and summary statistics |
| **PDF (.pdf)** | Multi-page PDF with monospaced text layout |
| **Word (.docx)** | Minimal OOXML document with bold headers and summary |

All columns exported including: Status, Title, Artist, File Name, File Path, Sample Rate, Bit Depth, Channels, Duration, File Size, Reported Bitrate, Actual Bitrate, Format, Max Frequency, Clipping, Clipping %, BPM, Replay Gain, Dynamic Range, MQA, MQA Encoder, AI, Fake Stereo, Silence, Date Modified, Date Created, True Peak, LUFS, Rip Log.

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
- **Battery Saver** — A Settings → Cache & Files performance mode that disables animations to save power: Now Playing backgrounds, visualizer, cover glow, lyric transitions, and waveform & playbar effects all stop. A single **Keep the audio visualizer running** option spares the visualizer if you still want it. Applies live, no restart
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

**Custom Fonts** — Choose from a built-in font list or add your own `.ttf` or `.otf` file in Settings → Appearance → App Font. The chosen font applies across the whole app, and custom files are copied into `%APPDATA%\AudioAuditor\Fonts\` so they keep working even if the original file is moved or deleted.

**ColorMatch Scope** — ColorMatch pulls the interface palette from the current album art. Now Playing and the main window can each be limited to just **backgrounds**, **buttons and icons**, and/or **text** rather than recoloring everything, and the Queue and Settings windows have their own independent toggles instead of following the main window.

### Settings Search
- A search box in the Settings header jumps straight to any setting by name, switching to the right tab and scrolling to highlight the match — no hunting through all seven tabs to find one toggle (Appearance, Playback, Analysis, Cache & Files, Export, Integrations, About)

### Your Wrapped
- **AudioAuditor Wrapped** — A single, roomy stats dashboard of your local listening and library stats: files scanned, hours listened, top artists/albums/tracks, unique albums, favorite formats, library quality, active date range/days active, average plays per track, and your most-listened track by time
- **100% local** — Stats are gathered entirely from your own plays/scans/analyses (opt-in collection), never uploaded, and can be reset anytime
- **Toolbar button** — Opens from a present/gift icon in the main toolbar (between Mini Player and the music-service buttons) and fills the current window instead of forcing fullscreen
- **Export** — Save the dashboard as a PNG, JPEG, or one-page PDF straight from the Wrapped window

### Sessions & Recovery
- **Session Restore** — AudioAuditor remembers which files and folders you had loaded and offers to reload them on the next launch
- **Crash Recovery** — A pending-recovery snapshot lets the app repopulate your working set after an unclean exit; pairs with the scan cache so the re-scan of unchanged files is instant

### Toolbar Customization
- **Optional toolbar buttons** — Settings → Appearance toggles let you hide the **Your Wrapped**, **Mini Player**, and **music-service** buttons if you don't use them (all shown by default)
- **Open With support** — Drag a file/folder onto `AudioAuditor.exe` or use Windows "Open with… → AudioAuditor" to load audio files, archives, playlists, or folders; if the app is already running, the items are forwarded to the existing window instead of being lost

---

## Getting Started

### Prerequisites

**To run AudioAuditor:**
- **Windows 10** or later (x64)
- **No runtime required** — the published executable is fully self-contained with the .NET 8 runtime embedded

**To build from source:**
- **.NET 8 SDK** or newer — check with `dotnet --version`
- Visual Studio 2022+ is optional; the `dotnet` CLI is enough
- The Windows GUI must be built on Windows. The CLI builds on Windows, Linux, and macOS

### Welcome Dialog
On first launch (or after a version update), a **Welcome dialog** appears with two things to set up. **Connection Mode** picks between Online (lyrics, AI detection, update checks, Last.fm, Discord) and Offline (no network at all — scanning, playback, EQ and everything else local still work); you can switch modes later in Settings. **Feature Highlights** then lets you enable or disable every analysis feature — Silence Detection, Fake Stereo, Dynamic Range, True Peak, LUFS, Clipping, BPM, MQA, Rip Log, AI Detection (default & experimental), and the SH Labs API. Disabled features are skipped during analysis and their columns are hidden from the results grid. The same toggles live in Settings → Analysis → Columns & Features, so you can change your mind at any time.

### CLI

AudioAuditor CLI is a standalone command-line tool built on the same analysis engine as the GUI — the scanner, its detectors, its verdicts, and its settings all live in the shared Core project, so a file scored on the CLI gets exactly the result the GUI would give it. On top of scanning it covers exports, metadata viewing/editing/enrichment, spectrograms, batch renaming, duplicate finding, AcoustID identification, and CD rip-log scoring. It ships as a single self-contained `.exe` — no .NET runtime or dependencies required.

> The GUI's interactive editing tools have no CLI equivalent: the Batch Editor's **Convert**, **Clean Up**, and **Paste metadata** tabs, **Transfer metadata between folders**, **Write Analysis to Files**, and **sheet music lookup** are Windows-only for now.

**Interactive Mode** — Launch with no arguments (or double-click the exe) to enter a persistent REPL session with colored prompts, `cd`/`ls`/`clear` navigation, and drag-and-drop path support:

```
audioauditor> scan "D:\Music\album"
audioauditor> info song.flac
audioauditor> export "D:\Music" -o results.csv
```

**CLI Commands:**

| Command | Alias | Description |
|---------|-------|-------------|
| `scan <path>` | `analyze` | Scan files or folders for quality |
| `info <file>` | — | Detailed analysis of a single file |
| `export <path> -o <file>` | — | Analyze and export results |
| `metadata <action> <file>` | `meta`, `tags` (interactive mode only) | View, edit, strip, or auto-enrich metadata |
| `spectrogram <path>` | `spectro` | Generate spectrogram PNG(s) |
| `rename <path>` | — | Batch-rename files from their tags (preview-first, `--dry-run`) |
| `duplicates <path>` | `dupes`, `dupe` | Find duplicate tracks in a folder |
| `identify <file>` | `id` | Identify a track via AcoustID fingerprint |
| `checklog <path>` | `riplog` | Score an EAC / XLD / whipper CD rip log |
| `credits` | — | Show open-source credits and licenses |
| `selfcheck` | — | Run the built-in assertion suite against this build (handy for confirming a portable download isn't corrupt) |

From a shell, version and help are flags rather than commands: `--version` / `-V` and `--help` / `-h`.

**Interactive-mode commands:** these work only inside the interactive shell (run `audioauditorcli` with no arguments), not as arguments from your own terminal.

| Command | Alias | Description |
|---------|-------|-------------|
| `config` | — | Guided config file editor (show / edit / reset / path) |
| `cd [dir]` | — | Change working directory; prints current if no arg |
| `ls` / `dir` | — | List files with color coding (green = audio, yellow = archive, cyan = dirs) |
| `clear` / `cls` | — | Clear terminal screen |
| `version` | — | Show version, runtime, and OS info |
| `help` / `?` | — | Show available commands |
| `exit` / `quit` / `q` | — | Exit interactive mode |

> **Tip:** In interactive mode, you can type or paste any valid file/folder path directly and it will automatically run `scan` on it.

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

**Analysis Flags (`scan` / `analyze`):**

Clipping, MQA, fake-stereo, and AI watermark detection run by default. The heavier measurements — silence, dynamic range, true peak, LUFS, and BPM — are **off by default** and must be switched on, either individually or all at once with `--thorough`.

```
--verbose, -v       Detailed per-file output
--json              Machine-readable JSON output (20+ fields per file)

  Enable the opt-in detectors (all off unless requested):
--thorough          Enable silence, DR, true peak, LUFS, and BPM together
--silence           Enable silence detection
--dynamic-range     Enable dynamic range measurement
--true-peak         Enable true peak measurement
--lufs              Enable integrated LUFS measurement
--bpm               Enable BPM detection
--experimental-ai   Enable spectral AI detection
--shlabs            Enable SH Labs AI detection

  Force the opt-in detectors back off (undoes --thorough or a cli-config.txt default):
--fast              Turn silence, DR, true peak, LUFS, and BPM all off
--no-silence        Disable silence detection
--no-dynamic-range  Disable dynamic range measurement
--no-true-peak      Disable true peak measurement
--no-lufs           Disable LUFS measurement
--no-bpm            Disable BPM detection

  Disable the on-by-default detectors:
--no-clipping       Disable clipping detection
--no-mqa            Disable MQA detection
--no-fake-stereo    Disable fake stereo detection
--no-ai             Disable AI watermark/metadata detection

  Other:
--rip-log           Score the EAC / XLD / whipper log next to the files and show a
                    Rip Log column (one cambia run per folder; opt-in, same as the GUI)
--always-full       Always run the full-file pass instead of sampling
--cutoff-allow      Enable the frequency-cutoff allowance
--no-cutoff-allow   Disable the frequency-cutoff allowance
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
--rip-log           Fill the Rip Log Score column from the folder's rip log
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

**Metadata Enrichment Flags (`metadata enrich <file-or-folder>`):**

Auto-fills missing tags from online sources, printing a per-file ✓/~/✗ line as each one finishes and listing anything unmatched at the end. MusicBrainz, iTunes, and Cover Art Archive are always on; the rest are opt-in. Soundtrack/OST context is detected automatically (composer filled, album artist set to "Various Artists" for compilations).

```
--all                    Overwrite existing tags too (default: fill missing only)
--acoustid               Also match by AcoustID fingerprint (needs a key)
--api-key <key>          AcoustID API key           (env: ACOUSTID_API_KEY)
--dry-run                Preview proposed changes without writing
-y, --yes                Apply without the confirmation prompt
--no-recursive           Do not recurse into subfolders

  Extra sources:
--deezer                 Also search Deezer       (no key needed)
--theaudiodb             Also search TheAudioDB   (no key needed)
--discogs-token <t>      Enable Discogs           (env: DISCOGS_TOKEN)
--fanarttv-key <k>       Enable fanart.tv         (env: FANARTTV_API_KEY)

  Streaming link → Comment field (opt-in; appended, never clobbers an existing comment):
--streaming-link <p>     Platform: deezer, apple, spotify, youtube
--spotify-id <id>        Spotify Client ID        (env: SPOTIFY_CLIENT_ID)
--spotify-secret <s>     Spotify Client Secret    (env: SPOTIFY_CLIENT_SECRET)
--youtube-key <k>        YouTube Data API key     (env: YOUTUBE_API_KEY)
```

**Spectrogram Flags (`spectrogram`):**

```
--linear              Use linear frequency scale instead of logarithmic
--difference          Render Left–Right channel difference instead of mono
--width <px>          Image width, 200–8000 (default: 1200)
--height <px>         Image height, 100–4000 (default: 400)
--all                 Generate for all files in a folder
```

**Stdin Pipe Support** — Pipe paths directly into the CLI: `echo "D:\Music" | audioauditorcli analyze` (capped at 50,000 paths). Both `analyze` and `export` accept piped input.

**Config File** — Place default flags in `%APPDATA%\AudioAuditor\cli-config.txt` (one flag per line) and they'll be applied automatically on every run. Run `config` in interactive mode for a guided setup wizard.

**Interactive Scan Controls** — During a scan, press keys without Enter:
- `p` — Toggle pause/resume
- `r` — Resume explicitly
- `q` or `s` — Stop/cancel early

Pause states are shown in the progress bar: `[PAUSED]`, `[FINISHING IN-FLIGHT...]` (while draining workers), or `[STOPPING...]`. Progress uses ANSI cursor positioning to redraw in-place; falls back to `\r` overwrite on legacy terminals.

**Archive Auto-Extraction** — Dropping or passing archive files (`.zip`, `.rar`, `.7z`, `.tar`, `.tgz`) automatically extracts them to a temp folder, scans the contents, and cleans up afterward. Protected against ZIP-slip attacks; capped at 50,000 entries and 5 GB total size.

**Scan Cache** — Results are cached to `scan_cache.json.gz` and reused on subsequent runs if the file size and modification time match, making re-scans of unchanged libraries nearly instant.

---

### CLI UI

When scanning, the CLI isn't just a boring progress bar:

- **🎬 Scanning Word Animation** — Every 9–13 seconds a new word is picked from a rotating vocabulary of 42 terms (*Analyzing, Scrutinizing, Inspecting, Dissecting, Audio-ing, Fingerprinting, Triangulating…*) and smoothly morphs into place letter-by-letter at ~14 letters/second. Suppressed with `--no-fun`.
- **⭐ Pulsing Star** — A Unicode star breathes in and out (`·` → `✦` → `✧` → `★`) next to the progress bar. Changes color to indicate state: **cyan** = running, **yellow** = paused, **red** = stopping.
- **💡 Random Tips** — One of 16 helpful tips appears ~30% of the time at scan start (e.g. *"Tip: Use --fast to skip dynamic range, true peak & LUFS for quicker scans."*). Suppressed with `--no-tips` or `--no-fun`.
- **🎉 Random Completion Messages** — One of 10 witty messages appears ~25% of the time after a scan finishes (e.g. *"All done! Your ears deserve the truth."*). Suppressed with `--no-fun`.
- **⏱️ ETA Display** — Pass `--eta` to see a live estimated time remaining. Calculated from a rolling 30-second completion window with exponential smoothing. Formats as `ETA <10s`, `ETA 45s`, or `ETA 2m 15s`. Default is off to keep the output clean.

**AI Detection Parity** — `analyze`, `export`, `info`, and `--json` output now include the same three-state AI verdict (Yes / Possible / No) and confidence score shown in the GUI. `info <file>` adds a leading `AI Detection: {Verdict} ({Confidence}% confidence)` line above the per-detector breakdown; `--json` adds `aiVerdict` and `aiConfidence` fields.

### Build from Source

Everything AudioAuditor ships can be built from this repository — you never have to trust a prebuilt binary. See the [FAQ](#faq) for why that matters.

**Clone, then build the Windows GUI:**

```bash
git clone https://github.com/Angel2mp3/AudioAuditor.git
cd AudioAuditor
dotnet build "Audio Quality Checker.sln" -c Release
```

> Name the solution explicitly. The repository root contains both a solution file and a project file, so a bare `dotnet build` stops with `MSB1011: Specify which project or solution file to use`.

To produce the same single-file portable `.exe` the releases ship (Windows only):

```bash
dotnet publish AudioQualityChecker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

### Build the CLI

The CLI is a separate project and builds on **Windows, Linux, and macOS** — it has no WPF dependency. One command produces a self-contained single-file binary with no .NET runtime needed on the target machine:

```bash
dotnet publish AudioAuditorCLI/AudioAuditorCLI.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

Swap `-r` for the platform you want:

| Target | `-r` value | Prebuilt release? |
|--------|-----------|-------------------|
| Windows x64 | `win-x64` | ✅ Yes |
| Linux x64 | `linux-x64` | ✅ Yes |
| Linux ARM64 | `linux-arm64` | ✅ Yes |
| macOS Intel | `osx-x64` | ❌ Build it yourself |
| macOS Apple Silicon | `osx-arm64` | ❌ Build it yourself |

The macOS targets are declared in the project file but are not part of the release pipeline and are not regularly tested — they may need adjustment.

The binary lands in `AudioAuditorCLI/bin/Release/net8.0/<rid>/publish/`. Add `-o <folder>` to send it somewhere else.

> **Note on bundled tools:** the repository does not include the FFmpeg or cambia binaries (they are third-party executables, not our code). A from-source build works fine without them, but audio conversion and the `checklog` command stay unavailable until you drop them into `third-party/ffmpeg/` and `third-party/cambia/`. See `third-party/ffmpeg/README.txt`.

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
- **Context Menu** — Right-click opens 5 submenus, plus Open File Location / Copy Path / Copy File Name at the top level:
  - **Favorites** — Star/unstar, Move Up, Move Down
  - **Metadata** — Batch Edit Tags, Batch Metadata Enrichment, Transfer metadata from another folder, Write Analysis to Files, Strip Metadata, Batch Rename, Quick Rename, Auto Rename (to "Artist - Title" or "Title - Artist"), Write Replay Gain
  - **Analyze / Identify** — Identify with AcoustID, Compare Waveforms, Compare Spectrograms, Find Duplicates, Check CD Rip Log, Find Sheet Music
  - **Spectrogram** — View Spectrogram, Save Spectrogram, View Album Cover
  - **File Operations** — Copy to Folder, Move to Folder, Save to Playlist
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

Settings live in seven tabs. Visualizer options sit inside the **Appearance** tab and Performance options inside **Cache & Files**; they are listed separately below only because there are enough of them to be worth calling out.

| Section | Options |
|---------|---------|
| **Appearance** | Color Theme (10 themes + Custom Theme editor), App Font (built-in list or your own `.ttf`/`.otf`), Playbar Theme (11 + Follow Theme), Visualizer Theme, Rainbow Visualizer Bars, Color Match with per-area scope (backgrounds / buttons & icons / text) and independent Queue and Settings toggles, Full Volume Visualizer, Reduce Motion, Now Playing visuals & background mode, Now Playing "Look Up This Song" services, optional toolbar buttons |
| **Playback** | Auto-Play Next, Audio Normalization, Crossfade (1–30s), Crossfade on Manual Skip, Gapless Playback, Spatial Audio, Loop Mode, Lyrics Auto-Save |
| **Analysis** | Toggle individual detectors (Silence, Fake Stereo, DR, True Peak, LUFS, Clipping, MQA, AI, BPM, Rip Log), Silence Threshold, Edge Skip Zone, Frequency Cutoff Allow, Always Full Analysis |
| **Visualizer** | Mode selection (6 modes), Auto-Cycle toggle with speed (5–60s), Custom cycle mode list, Independent theme, Full Volume rendering |
| **Cache & Files** | Scan Cache, Quick Rename Patterns, Default Folders, Focus Newly Added Songs, Session Restore, Battery Saver, Clear Favorites, Clear Cache, Reset Layout |
| **Export** | Default export format (CSV, TXT, PDF, XLSX, DOCX) |
| **Integrations** | Multi-service scrobbling (Last.fm, Libre.fm, ListenBrainz, Maloja) with thresholds, blacklist & pause, Discord Rich Presence, Offline Mode, AcoustID Fingerprinting |
| **Performance** | CPU usage limit — Auto, Low, Medium, High, Maximum; Memory limit — Auto, Low, Medium, High, Very High, Maximum; Hardware acceleration (Auto / Force software) |
| **About** | Version and build info, update check, open-source credits and licenses |

---

## Data & Privacy

AudioAuditor is designed with privacy in mind:

| Data | File | Location |
|------|------|----------|
| Theme preference | `theme.txt` | `%AppData%\AudioAuditor\` |
| Settings & options | `options.txt` | `%AppData%\AudioAuditor\` |
| Analysis result cache | `scan_cache.json.gz` | `%AppData%\AudioAuditor\` |
| Favorites | `favorites.json` | `%AppData%\AudioAuditor\` |
| Custom themes | `custom-themes.json` | `%AppData%\AudioAuditor\` |
| EQ profiles | `eq-profiles.json` | `%AppData%\AudioAuditor\` |
| Now Playing layout profiles | `np-layout-profiles.json` | `%AppData%\AudioAuditor\` |
| Album-art color cache *(opt-in disk persist)* | `np_color_cache.json` | `%AppData%\AudioAuditor\` |
| Listening stats *(Wrapped — opt-in)* | `stats.json.gz` | `%AppData%\AudioAuditor\` |
| Session restore & crash recovery | `last_session.json`, `recovery_pending.json` | `%AppData%\AudioAuditor\` |
| Anonymous install ID *(SH Labs opt-in only)* | `install_id.txt` | `%AppData%\AudioAuditor\` |
| API keys & credentials *(all services)* | `session.dat` *(DPAPI-encrypted)* | `Documents\AudioAuditor\` |
| SH Labs result cache *(opt-in only)* | `shlabs_cache.dat` | `Documents\AudioAuditor\` |
| SH Labs rate-limit counters *(opt-in only)* | `shlabs_usage.dat` | `Documents\AudioAuditor\` |
| Analyzed file data | Memory only | Not persisted — cleared on exit |
| Audio queue | Memory only | Not persisted — cleared on exit |
| Spectrograms | Memory only | Only saved if user explicitly exports |

`options.txt` stores theme names, boolean flags, service slot names, custom URLs/icons, EQ gains, scrobble thresholds, the per-song scrobble blacklist, and performance limits — no sensitive data. Every API key and credential the app holds is stored separately and **encrypted with Windows DPAPI** (AES-GCM with a keychain-held key on macOS and Linux) in `session.dat` in your Documents folder: the four scrobble services (Last.fm, Libre.fm, ListenBrainz, Maloja) plus Discogs, fanart.tv, Spotify, YouTube, AcoustID, Discord, and your own SH Labs key if you set one. All three SH Labs files are only created if you opt in to SH Labs detection; the install ID is a random GUID (not derived from any machine info) used solely for rate limiting (15/day, 100/month).

The table above lists what AudioAuditor keeps between sessions. It also writes a few incidental files: crash logs (`crash-*.txt`, opt-in), a donation-prompt day counter (`usage-days.txt`), an integrity log, a gapless-playback trace, downloaded `fpcalc.exe` for AcoustID, and any fonts you import — all under `%AppData%\AudioAuditor\`. Archive extractions and decode scratch files go to your temp folder and are cleaned up automatically.

**Network calls** — AudioAuditor makes network requests only in these specific situations:

| Trigger | Destination |
|---------|-------------|
| Click a music service search button | Opens the configured service (Spotify, Tidal, etc.) in your browser |
| Discord Rich Presence enabled | Discord IPC (a local process). Album art, when a Last.fm key is set, is fetched from `ws.audioscrobbler.com` |
| Scrobbling enabled *(per service)* | Last.fm / Libre.fm / ListenBrainz / your Maloja server |
| SH Labs AI detection *(opt-in)* | Cloudflare Worker proxy → `shlabs.music`. **The complete audio file is uploaded**, along with its filename, a SHA-256 hash, and a random install ID. The SH Labs API reads audio from a URL, so the proxy stages the file in temporary Cloudflare R2 storage behind a short-lived tokenised link and deletes it once the scan finishes |
| AcoustID fingerprinting *(user-initiated)* | `api.acoustid.org` + `musicbrainz.org`. First use also downloads `fpcalc` from `github.com` |
| Metadata enrichment / auto-tag *(user-initiated)* | `musicbrainz.org`, `itunes.apple.com`, `coverartarchive.org`, and — when enabled — `api.deezer.com`, `www.theaudiodb.com`, `api.discogs.com`, `webservice.fanart.tv`, `accounts.spotify.com`, `api.spotify.com`, `www.googleapis.com` |
| Lyrics lookup | `lrclib.net`, `music.163.com` |
| Lyric translation | `api.mymemory.translated.net` — **the lyric text is sent to this third party** |
| Sheet music lookup *(user-initiated)* | `imslp.org` |
| Update check *(on by default, silent on startup)* | `api.github.com` — the request carries no version; the comparison happens locally. Installing an update also downloads from `github.com` |

Every entry above is disabled by **Offline Mode**.

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
│       ├── AudioAnalyzer(.Quality/.Loudness/.BpmDetector/.Optimizer/.FullFilePass).cs  # analysis engine + FFT/spectral DSP (partials)
│       ├── AiWatermarkDetector.cs · ExperimentalAiDetector.cs · SHLabsDetectionService.cs  # AI detection
│       ├── MqaDetector.cs · AcoustIdService.cs · FlacReader.cs · AudioFormatReaders.cs
│       ├── LyricService.cs · MetadataEnrichmentService.cs · ExportService.cs · Equalizer.cs
│       ├── SpatialAudioProcessor.cs · CueSheetParser.cs · ShuffleEngine.cs · SmartRenameService.cs
│       ├── FavoritesService.cs · ScanCacheService.cs · IntegrityVerifier.cs · UpdateChecker.cs
│       ├── RipLogCheckService.cs · AudioConversionService.cs · SheetMusicLookupService.cs  # external-tool + lookup features
│       ├── BatchFieldEditService.cs · PastedMetadataService.cs · SourceJunkCleaner.cs · AnalysisTagWriteService.cs
│       ├── FileRenamer.cs · FilenameMetadataParser.cs · MinimalPdfWriter.cs · AppVersion.cs
│       ├── SelfChecks.cs · AiScoringSelfCheck.cs  # assert-based checks, run via the CLI `selfcheck` command
│       └── Scrobbling/                       # IScrobbler + Last.fm / Libre.fm / ListenBrainz / Maloja + ScrobbleManager
│
├── Services/                                 # Desktop-only services (WPF)
│   ├── AudioPlayer(.Crossfade/.Gapless).cs   # NAudio playback (partials)
│   ├── ThemeManager(.Brushes/.NowPlaying/.Persistence/.Scrobbling/.Visualizer/.Performance).cs  # theme engine (partials)
│   ├── AnimationPolicy.cs                     # Reduce Motion / Battery Saver gate
│   ├── SpectrogramGenerator.cs · MiniVisualizerRenderer.cs · SmtcService.cs
│   ├── DiscordRichPresenceService.cs · TranslateService.cs · EqProfileManager.cs
│   ├── SessionRestoreService.cs · LocalCrashLogger.cs · LocalStatsCollector.cs · CustomThemeStore.cs
│   └── WrappedExportService.cs                # Wrapped dashboard → PNG / JPEG / PDF
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
| [**NLayer**](https://github.com/naudio/NLayer) | 1.16.0 | Pure-managed MPEG/MP3 decoder — MP3 analysis on Linux/macOS and as a Windows fallback |
| [**TagLibSharp**](https://github.com/mono/taglib-sharp) | 2.3.0 | Audio metadata and tag reading (artist, title, bitrate, sample rate, BPM, Replay Gain, AI detection) |
| [**ClosedXML**](https://github.com/ClosedXML/ClosedXML) | 0.104.2 | Excel XLSX export with styled cells and formatting |
| [**SharpCompress**](https://github.com/adamhathcock/sharpcompress) | 0.48.0 | Archive extraction support |
| [**SkiaSharp**](https://github.com/mono/SkiaSharp) | 2.88.9 | Cross-platform 2D graphics for CLI spectrogram PNG generation |
| [**DiscordRichPresence**](https://github.com/Lachee/discord-rpc-csharp) | 1.2.1.24 | Discord Rich Presence client for playback status |
| [**Last.fm**](https://www.last.fm/api) / [**Libre.fm**](https://libre.fm/) / [**ListenBrainz**](https://listenbrainz.org/) / [**Maloja**](https://github.com/krateng/maloja) | — | Multi-service scrobbling and Now Playing updates |
| [**AcoustID / Chromaprint**](https://acoustid.org/) | — | Audio fingerprinting via fpcalc + MusicBrainz lookup |
| [**FFmpeg**](https://ffmpeg.org/) (LGPL build) | — | Audio format conversion in the Batch Editor's Convert tab, plus fallback decoding for AAC/M4A, ALAC, WMA, APE, WavPack, TAK, Musepack, and Speex |
| [**cambia**](https://github.com/arg274/cambia) | — | EAC / XLD / whipper rip-log parsing and OPS scoring for the CD Rip Checker |
| [**IMSLP**](https://imslp.org/) | — | Public-domain sheet music search via the MediaWiki API |
| **Windows DWM API** | — | Native title bar color theming via `DwmSetWindowAttribute` |
| **Windows SMTC** | — | System Media Transport Controls for media overlay integration |

---

## FAQ

<details>
<summary><strong>Why does Windows warn me with a blue "Windows protected your PC" screen?</strong></summary>

<br>

That's **Microsoft Defender SmartScreen**, and it is not a virus warning. It appears because AudioAuditor's binaries are not code-signed.

Code signing requires an Authenticode certificate — an ongoing yearly cost, and the OV/EV certificates that actually suppress the warning are the expensive tier. AudioAuditor is free, has no paid version and no ads, and is developed by one person in their spare time, so that cost isn't currently carried. On top of that, SmartScreen reputation is earned through download volume, which means even a signed build shows the warning until enough people have installed it — and the reputation partially resets with each new release.

To run it anyway: click **More info** → **Run anyway**.

**If you'd rather verify than trust**, you have real options — this is exactly why the project is open source:

1. **Check the hash.** Every release publishes an `AudioAuditor.exe.sha256` file. Compare it against your download:
   ```powershell
   Get-FileHash .\AudioAuditor.exe -Algorithm SHA256
   ```
   If it matches the published hash, the file you have is the file that was released.
2. **Scan it.** Upload the binary to [VirusTotal](https://www.virustotal.com/) and see the results from ~70 engines at once. Unsigned .NET single-file executables sometimes trip heuristic false positives from one or two engines — the aggregate view is the useful one.
3. **Read the source.** The entire application is in this repository under Apache-2.0. Nothing is hidden in a closed component.
4. **Build it yourself.** If you trust nobody, compile it — see [Build from Source](#build-from-source). A build you produced from source you can read requires zero trust in me or in any binary I publish.

Finally: only download from [audioauditor.org](https://audioauditor.org/) or [GitHub Releases](https://github.com/Angel2mp3/AudioAuditor/releases). Any other mirror is unofficial and unverified.

</details>

<details>
<summary><strong>Do I need to install .NET or any other runtime?</strong></summary>

<br>

No. The published GUI and CLI builds are self-contained — the .NET 8 runtime is bundled inside the executable. That's part of why the download is comparatively large.

The .NET SDK is only needed if you want to build from source.

</details>

<details>
<summary><strong>Is it actually free? What's the license?</strong></summary>

<br>

Yes, genuinely free. AudioAuditor is licensed under **Apache-2.0** — free to use, modify, and redistribute, commercially included, as long as you preserve the license and attribution.

There is no paid tier, no feature locked behind a purchase, no advertising, no account or sign-up, and no trial period. Support via [Ko-fi](https://ko-fi.com/angelsoftware) is entirely optional and unlocks nothing.

</details>

<details>
<summary><strong>Does AudioAuditor upload my music or phone home?</strong></summary>

<br>

**With one exception, no.** Every analysis AudioAuditor performs by default — spectral inspection, transcode detection, loudness, dynamic range, and the built-in AI detection — runs locally on your CPU, and your files stay where they are.

The exception is **SH Labs AI detection**, which is opt-in, off by default, and shows a privacy notice you have to accept before it runs. That feature uploads the complete audio file to the SH Labs API through a Cloudflare proxy, because the analysis happens on their servers. If you would rather nothing was ever uploaded, leave it off — the other AI detection modes are entirely local.

Beyond that, AudioAuditor makes network requests only for features that inherently require them, and only when you use them: AcoustID fingerprint lookups (user-initiated), metadata enrichment and cover-art lookups (user-initiated), lyrics and lyric translation, scrobbling (if you enable and log in), Discord Rich Presence, and the update check.

There is **no telemetry and no analytics**, and nothing you analyze or play is ever used to train generative AI. See [Data & Privacy](#data--privacy) for the full per-feature breakdown and a table of the files written to disk.

</details>

<details>
<summary><strong>How much should I trust the Real / Fake verdict?</strong></summary>

<br>

Treat it as **strong evidence, not proof**. AudioAuditor infers a file's history from what its audio actually contains — spectral cutoffs, energy above the cutoff, encoder artifacts, stereo structure, and related signals. That's a very good indicator, but no tool can read a file's true provenance from the audio alone, because the information genuinely isn't there.

A verdict of **Fake** means "this file's spectral signature is consistent with having been lossy at some point." Legitimate reasons a real lossless file can look suspicious include a genuinely band-limited master, a deliberate lowpass applied during mastering, source material recorded on limited equipment, and older releases that were legitimately mastered from a lossy or band-limited source. Conversely, a careful transcode at very high bitrate can be harder to catch.

The practical workflow: use the verdict as a filter, then **look at the spectrogram** before you delete or re-download anything. A hard horizontal line at 16 kHz across the whole track is a very different picture from a natural rolloff, and the spectrogram viewer makes that difference obvious in about a second.

</details>

<details>
<summary><strong>How reliable is the AI-generated music detection?</strong></summary>

<br>

It's marked **BETA** for a reason — treat it as a hint worth investigating, not a verdict to act on.

Detecting AI-generated audio is an unsolved and fast-moving problem: generation models change constantly, and every detector is chasing a target that moves. The output is a three-state verdict (**Yes / Possible / No**) with a confidence score rather than a yes/no flag, precisely because the uncertainty is real. Please don't use it as the sole basis for a takedown, a refund claim, or an accusation.

</details>

<details>
<summary><strong>Does it run on Linux or macOS?</strong></summary>

<br>

**The CLI does.** It's a full-featured command-line tool covering scanning, exports, metadata, spectrograms, renaming, duplicate finding, and AcoustID identification. Prebuilt self-contained binaries ship for **Windows x64, Linux x64, and Linux ARM64** — no runtime install needed.

For macOS, no prebuilt binary ships yet, but the project targets it and you can build one yourself in a single command — see [Build the CLI](#build-the-cli).

**The GUI is Windows-only.** It's built on WPF, which is a Windows-only framework, so the desktop app cannot run on Linux or macOS. A cross-platform GUI is in development but is not ready or released yet.

</details>

<details>
<summary><strong>Does the CLI give the same results as the GUI?</strong></summary>

<br>

Yes, exactly the same. Both are thin front-ends over one shared analysis engine in the `AudioAuditor.Core` project — the same detectors, thresholds, verdict logic, and settings. The CLI is not a stripped-down or lagging version, and a file scored in one will get the identical verdict in the other. Only presentation differs.

Some *interactive editing* tools are GUI-only (the Batch Editor's Convert, Clean Up, and Paste-metadata tabs, metadata transfer between folders, Write Analysis to Files, and sheet music lookup), but the analysis itself is identical.

</details>

<details>
<summary><strong>Will it modify or damage my audio files?</strong></summary>

<br>

Analysis is strictly **read-only** — scanning opens your files, reads them, and writes nothing back.

Files are only ever written when you explicitly invoke a tool that is meant to write: tag editing, batch metadata edits, renaming, format conversion, or Write Analysis to Files. Batch rename is preview-first (and the CLI has `--dry-run`), so you see exactly what will change before anything happens.

</details>

---

## Support & Supporters

AudioAuditor is built and maintained in my free time. If you find it useful, consider supporting development so I can keep adding features, improving performance, and squashing bugs.

<a href="https://ko-fi.com/angelsoftware">
  <img src="https://img.shields.io/badge/Support_on-Ko--fi-f26b2e?style=for-the-badge&logo=ko-fi&logoColor=white" alt="Support on Ko-fi"/>
</a>

---

## Contributing

AudioAuditor is an open-source project developed by me in my spare time. Suggestions, feedback, and issue reports are always welcome!

**v2.0 is out, and pull requests are open again.** The pause during the v2.0 rewrite ([issue #6](https://github.com/Angel2mp3/AudioAuditor/issues/6)) is over.

---

### Getting Started
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

### Fonts

Bundled with the cross-platform build only. The Windows app's font picker lists families already installed on your system and ships no font files of its own.

| Font | License | Usage |
|------|---------|-------|
| [**Selawik**](https://github.com/microsoft/Selawik) by Microsoft | SIL OFL 1.1 | Metric-compatible Segoe UI substitute — the UI font on Linux and macOS, where Segoe UI cannot be redistributed |
| [**Inter**](https://github.com/rsms/inter) by The Inter Project Authors | SIL OFL 1.1 | Bundled UI typeface for the cross-platform build |

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
  <a href="#top"><kbd>⬆️ Back to Top</kbd></a>
</p>

<p align="center">
  <sub>Built with ❤️ by Angel for audiophiles who care about quality</sub>
</p>
