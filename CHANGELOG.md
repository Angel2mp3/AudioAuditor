# Changelog

### New Features

#### ZIP Archive Support
- Audio files inside `.zip` archives can now be analyzed directly in both GUI and CLI
- GUI: "Add Files" dialog filter includes `*.zip`; drag-and-drop and "Add Folder" also detect archives
- Archives are extracted to a temporary directory and cleaned up automatically
- CLI: `analyze`, `export`, and `info` commands all support ZIP inputs

#### Visualizer Improvements
- Rainbow visualizer moved from standalone toggle to playbar dropdown menu
- Pre-allocated FFT buffers (`_vizReal`, `_vizImag`, `_vizMags`, `_vizBarValues`) to eliminate per-frame GC allocations
- Cached `PlaybarColors` in `ThemeManager` to avoid 60 dictionary allocations per second during rendering

#### Bitrate Scanning Overhaul
- Codec-aware cutoff-to-bitrate estimation with separate mapping tables for MP3/LAME, AAC, Opus, and Vorbis
- Multi-scale spectral band analysis using two band sizes (1500 Hz and 2500 Hz) for more accurate cutoff detection
- Finer frequency stepping and lower 4 kHz start threshold for detecting subtle lowpass filters
- Lossless files now report uncompressed PCM data rate when spectrum reaches Nyquist
- Lossy bitrate selection: uses reported value when spectral estimate matches within 85–120%, otherwise uses the lower value as the quality bottleneck

### UI Changes
- Removed music note (♪) from Cover/Hide toggle button text

### Documentation
- Expanded README credits with NAudio.Vorbis, Concentus & Concentus.OggFile, Fisher-Yates algorithm, HRTF concepts, LAME lowpass specifications, Last.fm API, and metadata search services (MusicBrainz, Discogs, AllMusic, Rate Your Music)

### Project Structure
- Reorganized project files into logical folders:
  - Moved all window files (`MainWindow`, `SettingsWindow`, `QueueWindow`, `ErrorDialog`) into `Windows/` folder
  - Moved all resource images (`icon.png`, `app.ico`, service logos) into `Resources/` folder
  - Updated `App.xaml` StartupUri, `.csproj` resource references, and all pack URIs to match new structure

#### Metadata Editor
- Added a full metadata editing window accessible via right-click context menu → "Edit Metadata..."
- Editable fields: Title, Artist, Album, Album Artist, Year, Track Number, Total Tracks, Disc Number, Total Discs, Genre, BPM, Composer, Conductor, Grouping, Copyright, Comment, Lyrics
- Album cover management: preview, replace from image file, or remove entirely
- **Search Metadata Online**: quick-search buttons for MusicBrainz, Discogs, AllMusic, and Rate Your Music to look up track metadata
- "Strip All Metadata" option to remove all tags from a file
- Metadata reading/writing powered by [TagLibSharp](https://github.com/mono/taglib-sharp) (MIT License)

#### Improved Shuffle
- Replaced history-based random shuffle with Fisher-Yates deck-based algorithm
- Guarantees every track plays exactly once before any repeats
- Deck automatically rebuilds when exhausted or track list changes
- Currently playing track is never the first track in a new deck

#### Multi-Folder Selection
- "Add Folder" dialog now supports selecting multiple folders at once
- All selected folders are scanned and their audio files added in a single operation

#### CLI Tool (AudioAuditorCLI)
- Created a standalone command-line interface for AudioAuditor
- Commands:
  - `analyze` — Analyze audio files/folders for quality (fake lossless, clipping, MQA, AI detection)
    - Options: `--verbose`, `--status <filter>`, `--threads <n>`, `--recursive`, `--json`
  - `export` — Analyze and export results to CSV, TXT, XLSX, PDF, or DOCX
  - `metadata` — View or edit audio file metadata
    - Actions: `show`, `set`, `remove-cover`, `strip`
    - Supports all metadata fields including BPM, conductor, grouping, copyright, lyrics
  - `info` — Detailed single-file analysis with colored output
- Shares core analysis engine with the GUI application via linked source files

### Bug Fixes
- Fixed spatial audio processing strength and HRTF application
- Fixed service logo icons (Qobuz, Amazon Music, Apple Music, etc.) not displaying — double `Resources/Resources/` path in pack URIs caused all PNG icons to silently fail and fall through to vector icons
- Fixed application crash on startup when running from `Windows/` folder — window `Icon` path was relative and resolved incorrectly after file reorganization
- Fixed CLI `--help` flag intercepting subcommand help (e.g., `analyze --help` was showing main help instead of analyze-specific help)
- Fixed CLI help examples: added `.\` prefix required by PowerShell, added note about quoting paths with special characters (`(`, `)`, `'`)
- Removed stale root-level `MainWindow.xaml` and `MainWindow.xaml.cs` left over from reorganization that caused 221 WPF temp project build errors

### Internal
- Extracted `OpusFileReader` and `DsdToPcmReader` classes from `AudioPlayer.cs` into dedicated `Services/AudioFormatReaders.cs` to allow shared use by CLI without WPF dependencies
- Added CLI project exclusion rules to main `.csproj` to prevent WPF temp project compilation conflicts