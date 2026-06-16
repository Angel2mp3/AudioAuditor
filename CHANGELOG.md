## v1.8.0 (latest)

### New Features

#### Scrobbling — Multi-Service Support
- **Libre.fm support** — scrobble to Libre.fm, the free and open-source Last.fm alternative. Authenticate with your Libre.fm credentials in Settings → Integrations.
- **ListenBrainz support** — scrobble to ListenBrainz, the open-source music listening tracker by MetaBrainz. Enter your user token in Settings → Integrations.
- **Maloja support (self-hosted)** — scrobble to your own [Maloja](https://github.com/krateng/maloja) server via its ListenBrainz-compatible submit endpoint. Enter your server URL and API key in Settings → Integrations; the API key is stored encrypted (DPAPI) like the other credentials, and your Maloja profile shows up in the ♫ widget's Profiles menu alongside the others.
- **All four services run together** — Last.fm, Libre.fm, ListenBrainz, and Maloja all scrobble simultaneously. Each can be enabled/authenticated independently in Settings → Integrations, and a single playthrough fans out to every active service.
- **Configurable scrobble thresholds** — replaces the previous hardcoded "50% OR 240s" rule. Set `Scrobble at percent`, `Scrobble at seconds`, and `Minimum track length` in Settings → Integrations; the first rule met fires the scrobble. Set a value to `0` to disable that rule.
- **Anti-duplicate by max position reached** — seeking past the threshold and back never re-triggers a scrobble within the same play; the manager tracks the furthest position the song has reached, not the current position.
- **Pause All Scrobbling toggle** — global on/off from Settings → Integrations or the new corner widget popup. While paused, no service receives now-playing or scrobble events.
- **Per-song blacklist** — "Never Scrobble This Song" from the corner widget adds the current artist + title to a blacklist (persisted in `options.txt`). Matching is **cross-library by Artist|Title**, so duplicate copies of the same song in different folders all stay un-scrobbled.
- **Don't Scrobble Current Song** — one-time skip for the current play without blacklisting; useful for previewing a track you don't want to count.
- **Scrobble Now** — manually push the current track's scrobble immediately, regardless of threshold.
- **Corner status widget** — new bottom-right ♫ icon in the main window status bar (replacing the old "Last.fm: Not Connected" text indicator), with a label to its left showing "Scrobbling" / "Paused" / "Offline" / "Not connected". Click to open the scrobble menu: dynamic per-service profile links, one-click Scrobble Now / Don't Scrobble / Never Scrobble This Song, and the global pause toggle. The widget uses rounded, subtle accent hover states and muted opacity when paused or offline.

#### Now Playing — Background Animations
- **Stars** — independent per-star twinkle phase, gentle parallax drift, a wider size/brightness spread, and rare soft-bloom bright stars.
- **Shooting Stars** — sporadic streak scheduler: occasional meteors with a tapered glowing tail, a bright bloom head, randomized entry edge/angle, and a clean fade-out.
- **Color Drift** — a slow, smooth ambient color gradient that shifts with the album palette.
- **Rain** — angled wind-blown streaks with varied length/opacity/speed and an optional **lightning** flash (tasteful double-flicker, off-able, frequency configurable).
- **Snow** — soft drifting flakes with per-flake sinusoidal sway and a configurable size/large-flake mix.
- **Leaves** — autumn leaves tumbling and swaying on the wind (shares the Snow density control).
- **Underwater** — a calm deep-sea scene: slowly rising bubbles, drifting blue/teal light shafts, swaying seaweed, and the occasional fish silhouette.
- **Configurable, theme-matched controls** — Settings → Appearance → Now Playing Visuals exposes a mode picker (Off / Color Drift / Stars / Shooting Stars / Rain / Snow / Leaves / Underwater), two sliders per effect plus the lightning toggle, and a global animation-speed slider. All sliders tint to the active theme and live-apply.
- **Auto-cycling** — optionally cycle through background modes automatically, including switching on each song change, at a configurable speed.

#### Mini Player
- **Floating mini player** — a compact, draggable, always-on-top window (toolbar **Mini Player** button) with cover art, title/artist, transport, seek, volume/mute, and shuffle. It has its own optional inline visualizer that runs independently of the main window, and the window grows/shrinks as you toggle it. Always-on-top preference is remembered.

#### Now Playing — ColorMatch Eyedropper
- New **eyedropper icon** in the Now Playing player bar (inside the **Color options** flyout, next to the ColorMatch toggle). Click to enter picker mode, then click anywhere on the album cover to sample that pixel's color. Choose how many colors to pick — **3 to 6** per track (default 3) — with the picker-count stepper in the Color options flyout.
- **Picked colors override the auto-extracted album palette** — the background gradient, bottom bar tint, title highlight, buttons, icons, seek bar, volume bar, labels, active toggles, and visualizer colors all immediately switch to the picks. The first three picks drive the palette (icons, glow, visualizer); any extra picks enrich the background gradient as additional stops.
- **Clean picker flow** — the picker stays active until you've made the chosen number of picks, then closes and clears the cover cursor/hover state. Click the eyedropper again to start a fresh session.
- **Right-click the eyedropper to reset** and revert to auto-extracted colors.
- **Per-track state** — overrides stay session-only unless the disk color cache option is enabled, in which case picked colors are saved using hashed cache keys. The chosen pick count persists in `options.txt`. Visualizer colors are clamped to a minimum luminance so dark picks still glow.

#### Equalizer — Profiles
- **Built-in EQ presets**: Flat, Bass Boost, Vocal, Rock, Pop, Jazz, Classical, Electronic — pick from a new dropdown in the EQ panel and the bands jump to the preset shape with the new gains applied to the current track immediately.
- **Save current as a custom profile** — "Save..." button next to the dropdown prompts for a name, snapshots the current 10-band gains, and persists them to `%APPDATA%/AudioAuditor/eq-profiles.json`. Custom profiles appear at the bottom of the dropdown after a separator.
- **Delete a custom profile** — when a custom profile is selected, a Delete button appears alongside Save. Built-in profiles cannot be deleted or overwritten.
- **Auto-detect current shape** — opening the EQ panel auto-selects the matching profile if the saved gains exactly match a built-in or custom one, otherwise leaves the dropdown on Flat.

#### Batch Metadata Editor
- **Multi-file tag enrichment (GUI)** — select files in the grid and open **Batch Metadata** to fetch missing tags from online providers (MusicBrainz and others). Choose which fields (title, artist, album, album-artist, year, track/disc, genre, composer, lyrics, cover art…) and providers to use, **preview every proposed change** in a grid, then apply. "Missing-only" by default so existing tags aren't clobbered.

#### Custom Themes
- **Build your own theme** — a theme editor in Settings → Appearance: name it, set the palette, and watch a live preview update as you drag the controls. Saved custom themes persist and appear alongside the built-ins in the theme picker, and can be re-edited or deleted (built-ins can't).

#### Your Wrapped
- **AudioAuditor Wrapped** — a big, single stats dashboard of your local listening and library stats (files scanned, hours listened, top artists/albums/tracks, favorite formats, library quality, and more). Stats are gathered 100% locally from your plays/scans/analyses, and can be reset anytime.

#### Session Restore & Crash Recovery
- **Reload your last session** — AudioAuditor remembers which files/folders you had loaded and offers to restore them on the next launch.
- **Crash recovery** — if the app exits abnormally it leaves a recovery marker and offers to bring back your previous session (plus a crash snapshot) the next time you open it.

#### Visual Customization
- **Main-window background image** — set a custom image behind the main window, with adjustable blur and opacity.
- **Now Playing backdrop** — use the album art, a custom image, or custom colors as the Now Playing background, with blur, brightness, zoom, and position controls.
- **Cover shape** — choose the album-cover shape in Now Playing and the Mini Player.
- **Playbar styles** — pick a playbar animation style, applied across the main window, Now Playing, and the Mini Player.

#### Now Playing — Layout Profiles
- **Single-row player bar** — the bottom player bar keeps transport controls, auto-play, secondary tools, volume, and Back in one row, with long song/artist text trimmed before controls can overlap.
- **Visualizer-aware layout profiles** — Now Playing saves separate layout profiles for windowed/fullscreen with the visualizer on or off, including user-adjusted sizes, offsets, visualizer height, and visualizer placement in the user settings file.
- **Standalone bottom-bar alignment** — artist text in the standalone Now Playing bottom bar sits slightly lower so it lines up more naturally with the song title.

#### Now Playing — Customize Layout
- **Compact, collapsible menu** — the Customize Layout popup was far too long. It's now organized into collapsible sections (Layout Profiles, Bottom Bar Buttons, Glow Options, Backdrop, Sizes, Position Offsets), collapsed by default so you expand only what you need. Reset to Default stays pinned at the bottom.
- **No more runaway width** — expanding the "Bottom Bar Buttons" section used to stretch the popup absurdly wide (a big gap between each button label and its move arrows) because the panel had no width bound. It's now a fixed, compact width that wraps text and keeps the rows tight; height still scrolls within the popup.
- **Album Cover Glow slider** — new size slider in the Customize Layout popup. `0` removes the glow around the album cover entirely, `1` is the default soft halo, and values up to `2.0` make the glow noticeably larger. Setting persists to user settings (`NpCoverGlowSize`) so it survives restarts. The breathing-pulse animation respects the new scale.

#### Now Playing — Look Up This Song
- **Independent search services** — the Now Playing "look up this song" magnifier now has its **own** configurable service list, separate from the main window's search buttons. Set up to 6 services, pick which appear (uncheck "Show" to display fewer), and configure custom search URLs/icons — all in Settings → Appearance → "Now Playing — Look Up This Song".
- **Copy from main window** — one button seeds the Now Playing services from your existing main-window setup as a starting point. (New installs and existing configs are seeded automatically on first run, so nothing looks empty.)

#### Performance & Accessibility (Desktop)
- **Reduce motion** — the Settings → Appearance "Enable UI animations" toggle is now **"Reduce motion"**, and it's comprehensive: it stops Now Playing backgrounds, cover glow, lyric transitions, and playbar effects **and** the audio visualizer (both the main and mini-player visualizers, which previously kept animating). One switch to calm the whole app on lower-end hardware.
- **Battery Saver** — new Settings → Cache & Files → Performance mode that disables animations to save power. A master toggle plus per-area checkboxes (Now Playing backgrounds, audio visualizer, cover glow, lyric transitions, waveform & playbar effects) and an **"Entire program (all areas)"** option. Applies live, no restart; manual on/off.
- **Hardware acceleration control** — new render-mode selector (Auto / **Force software (CPU only)**) for machines with flaky GPU drivers, plus a read-out of the detected render tier. Applies on restart.
- **Lighter blurred backdrops** — the main-window and Now Playing album backdrops are now GPU-cached (BitmapCache), so their heavy blur isn't recomputed every animation frame while particles/gradients move over them.
- **Lighter oscilloscope visualizer** — the Scope visualizer style no longer allocates a fresh point buffer (~one per horizontal pixel) every frame; it reuses a single buffer and only rebuilds it when the window is resized, cutting steady 60fps GC churn on the Now Playing screen.

#### Main Window — Toolbar
- **Optional toolbar buttons** — new Settings → Appearance toggles let you hide the **Your Wrapped** button, the **Mini Player** button, and the **music-service** buttons from the main toolbar if you don't use them. All three are shown by default.

---

### Coming Soon
- A browser version of AudioAuditor is in development and should be available in the next few months.
- A **macOS CLI** is also coming soon.

---

### Improvements & Fixes

#### Now Playing — Color Options
- **"Color match" button is now "Color options"** — the Now Playing toolbar color button (and its flyout) is renamed to Color options, and the flyout now hosts a themed picker-count stepper alongside the existing Color Match toggle and eyedropper.

#### Playback — Loading & Gapless
- **No more "is it frozen?" on heavy files** — track loading (decoder open + duration scan) used to run on the UI thread, so a large FLAC, a VBR MP3 that needs a full duration scan, or a slow-to-parse container could freeze the window for a few seconds and look like a crash. Loading now happens on a background thread (serialized so rapid skips don't overlap), keeping the UI responsive while a track loads.
- **Earlier gapless pre-buffer** — the next track is now prepared a little earlier (when ~5s remain instead of 3s) so it's ready in time for a seamless switch even on slower files.

#### Settings — Window
- **Drag from anywhere** — the Settings window can now be moved by click-dragging any empty area of it, not just the title strip. Clicks on buttons, tabs, sliders, combo boxes, list items, and text fields still work normally, and the close button no longer starts a drag.

#### Settings — Credits & Licenses
- **New credits window** — credits all the open-source projects this app uses, with a **View license** button for each that opens its full license/notice text, shipped with the app in a `Third.Party.Notices` folder.

#### GUI — Lyrics
- **Lyric line-change animation no longer interrupts itself** — the catch-up retry was setting `_npCurrentLyricIndex = -1` and re-calling the highlighter on every tick, even when the first call already advanced the line successfully. That double-call cancelled the in-flight `DoubleAnimation`/`ColorAnimation` mid-transition. The retry now only re-runs when the first call hasn't advanced, so the smooth easing curve is preserved for line changes.
- **Blur-mode lyric blur returns after minimize/restore** — fixed: minimize → restore used to leave the Lyrics mode showing enabled but inactive lines visually un-blurred. `ResumeAnimations` and `UpdateLyricsWorkState` now explicitly re-apply blur effects on restore.
- **Provider fallback behavior** — Auto lyrics now keeps trying local, LRCLIB, and Netease before showing "none found," prefers timed lyrics over plain text when available, and applies the conservative censored-lyrics fallback only when the option is enabled.
- **Translated synced lyrics** — translation rebuilds preserve synced lyric timing and immediately re-run highlight/scroll against the current playback position.
- **Standalone Now Playing lyric sync** — the separate Now Playing window uses the same delayed catch-up behavior as the main Now Playing surface so synced lyrics start highlighting and scrolling after load.

#### GUI — Lyrics (Timing)
- **Tighter synced-lyric tracking** — the active lyric line was advanced only by the 50 ms Now Playing update timer, a `DispatcherTimer` that runs below rendering priority and could be delayed by heavy background animations — so the highlight sometimes lagged or desynced under load and was fine when idle. While timed lyrics play, the highlight is now also driven from the per-frame render loop at the live playback position (it early-returns when the line hasn't changed, so it stays cheap), and the decorative Color Drift gradient timer is throttled (imperceptibly) while lyrics track, freeing UI-thread time. Some lag inherent to the audio buffer remains (covered by the existing 200 ms look-ahead), but tracking is noticeably steadier.

#### Playback — Persistence
- **Volume actually remembers** — fixed: the bottom-right Now Playing volume slider had a hardcoded XAML default of `Value="80"` that overwrote the loaded value when the NP panel hadn't been shown yet. Removed; the slider now picks up the saved value cleanly on launch.

#### Playback — Up Next
- **Shuffle-aware Up Next** — fixed: when shuffle was on, the "Up Next" preview showed the current shuffled track, not the next one. It now reads `_shuffleDeckIndex + 1` and wraps to `_shuffleDeck[0]` when looping all.

#### Stability — Crash Logging
- **Full-coverage crash logging** — crash logging now captures WPF UI-thread exceptions (`DispatcherUnhandledException`) and unobserved background-task exceptions (`TaskScheduler.UnobservedTaskException`) in addition to `AppDomain.UnhandledException`. Previously a crash on the UI thread (e.g. opening certain files) left no log at all, making reports undiagnosable.
- **On by default, with an opt-out** — local crash logging is now enabled by default (it was opt-in). The first-run/upgrade Welcome dialog explains it and lets you turn it off, and the Settings toggle still works. Logs stay 100% local and have file paths redacted.

#### Stability — Multichannel / Dolby Atmos Playback
- **Verified multichannel-safe** — the playback DSP chain (Equalizer → Spatial Audio) and the file analyzer were confirmed against 5.1 / 7.1 / 7.1.4 audio (AAC, E-AC-3, ALAC): unsupported or undecodable tracks now fail gracefully (auto-skip with a status message, or a single error dialog) instead of taking the app down. Added an automated multichannel regression test so this can't quietly break again.

#### Settings — Persistence Reliability
- **Column layout saves on change, not just on exit** — column order and widths are now persisted (debounced) whenever you reorder or resize a column, instead of only when the app closes cleanly. A crash no longer wipes your grid layout.
- **Settings-save failures are no longer silent** — the options-file save/load and column-layout save paths previously swallowed every exception, so a failed write silently lost your settings with no trace. These now write a local crash log so the cause is visible.

#### Audio Analysis — ALAC
- **ALAC bit depth & bitrate now reported** — Apple Lossless files in `.m4a`/`.mp4` containers previously showed blank bit depth and bitrate because TagLib reports `0` for ALAC. AudioAuditor now parses the ALAC magic-cookie atom (`ALACSpecificConfig`) to read the real bit depth, sample rate, channel count, and average bitrate.

#### Audio Analysis — MQA
- **Fixed MQA false positives** — the embedded-MQA scanner flagged a file the moment it saw the 36-bit sync word **once**. Across 8 bit positions and ~132k samples that's a real (~1 in 65,000) chance of a random match in ordinary lossless files, so large libraries collected phantom "MQA" tags. Detection now requires the sync word to **recur at least 3 times** at the same bit position — which genuine MQA always does (the sync repeats every frame), while a chance collision does not. Backed by unit tests covering single/double/triple sync occurrences and random-noise input.

#### Packaging — Setup Installer
- **New Windows installer** — alongside the existing portable `.exe`, there's now a proper Inno Setup installer (`AudioAuditor-Setup-<version>.exe`) with Start Menu/desktop shortcuts, an uninstaller, optional file associations, and a choice of per-user or all-users install. Build both with `scripts\Build-Installer.ps1`; the winget package now uses the installer.

#### Under the Hood
- **Codebase split for maintainability** — many large source files were broken into focused partials (Now Playing, ThemeManager, Settings, MainWindow, the analyzer) with no behavior change, making the app easier to evolve safely.
- **Automated test suites** — new Core and Windows test projects now guard the analyzer settings, color pipeline, lyric matching/timing, shuffle, smart-rename, theme persistence, multichannel DSP, and MQA detection against regressions.

## v1.7.0

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
