## v2.0.0 (latest)

### New Features
- **Settings search** — a search box in the Settings header jumps straight to any setting by name, switching tabs and scrolling/highlighting the match, so you no longer have to hunt through all 7 tabs.
- **Sheet music lookup** — right-click a file (or several) → **Analyze / Identify → "Find Sheet Music..."**, or the new button on the Now Playing bar, searches IMSLP's public-domain/classical catalog for real matches with real download links, stepping through multi-selections one at a time with Prev/Next. Anything outside IMSLP's catalog (most modern/pop songs) falls back to a one-click web search.
- **ColorMatch scope controls** — Now Playing and Main Window ColorMatch can each be restricted to just **Backgrounds**, **Buttons & icons**, and/or **Text** (Settings → Appearance → "ColorMatch Scope"), instead of recoloring everything. The Queue and Settings windows also got their own independent ColorMatch toggle instead of always following the Main Window's.
- **Custom fonts** — pick from a built-in font list or add your own `.ttf`/`.otf` file (Settings → Appearance → "App Font"); the chosen font applies across the whole app and custom files are copied into `%APPDATA%\AudioAuditor\Fonts\` so they survive the original file being moved or deleted.
- **CD Rip Checker (replaces the old "Rip Quality" detector)** — the previous signal-based rip check (which guessed at rip integrity from the waveform) is gone; reading the actual **CD ripping log** is the right way to verify a rip. AudioAuditor now scores EAC / XLD / whipper logs with the OPS deduction model via the bundled [cambia](https://github.com/arg274/cambia) checker, showing the 0–100 score and every deduction. Three ways to use it: a dedicated **Tools → Check CD Rip Log…** window (drop a `.log` or browse), the `checklog` / `riplog` CLI command, and an opt-in scan-time **Rip Log** column that auto-detects the log sitting next to your files (one cambia run per folder). The old `--rip-quality` flag and "Rip Quality" column/setting are removed.
- **Batch Editor Overhaul - Many more options and bundled some previous settings into the same one while still keeping some of the older simple editor style window(s) for individual audio files. 
  - **Manual Edit** — set any tag field (Title, Artist, Album, Album Artist, Year, Genre, Composer, Comment, Disc) across the whole selection at once. Only the fields you check are written; everything else is left untouched. Includes track-number tools (set all to one number, or auto-number in list order) and full batch **album-cover** control: pick one image for all, fetch covers online per album, or strip covers from all. Also has a one-click **"Fill missing fields online"** that searches the metadata providers and auto-applies high-confidence matches to *empty fields only*, leaving everything you've already set untouched.
  - **Auto-Tag BETA** — auto-searches MusicBrainz / iTunes / AcoustID + Cover Art Archive on open, pre-selects high-confidence matches, and adds a one-click **"Apply High-Confidence"** button so a folder of files can be tagged in a single action (still fully reviewable before writing). Auto-tagging now also parses messy/untagged filenames (junk-stripped, "Artist - Title" split) so files with little or no existing metadata match far more reliably.
    - **More sources** — added **Deezer** and **TheAudioDB** (no key needed, on by default; great for covers and soundtrack coverage), plus optional **Discogs** and **fanart.tv** that activate once you enter a token/key (remembered between sessions; environment variables `DISCOGS_TOKEN` / `FANARTTV_API_KEY` are also honored). When the best match is missing a cover, genre, or composer, one is borrowed from another source's result.
    - **Soundtracks, OSTs & scores** — the matcher now detects soundtrack context (from the album/folder name or a "Various Artists" artist), prefers soundtrack-typed releases, fills the **Composer** field, sets **Album Artist** to "Various Artists" for compilations, and gives soundtrack candidates a scoring nudge so an OST wins over a same-titled pop single.
    - **Live results** — files now report **success/failure as each one finishes** (with the matched source), instead of the grid sitting empty until the whole batch is done. Results and cover thumbnails stream into the list progressively, the status bar shows a running matched/unmatched tally, and a **"⚠ unmatched"** button lists exactly which files couldn't be matched and why. Searching also runs several files in parallel (respecting each provider's rate limit) so large batches finish faster.
    - **Cover preview** — proposed cover art is shown as a thumbnail in the changes list so you can vet artwork before it's written.
    - **Streaming link → Comment** — optionally look the track up on a streaming service and embed its URL in the Comment field (and, for MP3s, an ID3v2 `WXXX` URL frame). Pick the platform per run: **Deezer** and **Apple Music** work with no setup; **Spotify** activates once you enter a Client ID + Secret, and **YouTube** with a Data API key (all remembered between sessions; env vars `SPOTIFY_CLIENT_ID`/`SPOTIFY_CLIENT_SECRET`/`YOUTUBE_API_KEY` honored). The link is appended without clobbering an existing comment. CLI: `--streaming-link <deezer|apple|spotify|youtube>` with `--spotify-id`/`--spotify-secret`/`--youtube-key`.
  - **Rename** — exposes the custom-pattern engine with more tokens (`{albumartist}`, `{track3}`, `{disc}`, `{genre}`), case modifiers (`{title:upper|lower|title}`), configurable track zero-padding, a find/replace box, and **inline-editable** proposed names. Now also has whole-name **transforms** that apply in both manual and smart modes: a case dropdown (as-is / lower / UPPER / Title), a spaces dropdown (keep / underscores / spaces), and a **Strip "(feat. …)"** toggle — all remembered between sessions and reflected live in the preview.
  - **Convert** — a new **Convert** tab converts the selected files between formats (MP3, FLAC, WAV, AAC/M4A, OGG Vorbis, Opus, WMA, AIFF) using **FFmpeg** — placed next to the app or installed on your PATH — carrying tags and embedded cover art over (`-map_metadata 0`) where the target supports them. Per-codec quality/bitrate controls, a choice of output folder (or in-place), overwrite and "delete originals" options, and a live preview of the resulting filenames. FFmpeg is run as a separate process alongside AudioAuditor, if no FFmpeg is found the tab shows a notice and everything else keeps working.
  - **Clean Up** — strips download, source, and sponsor junk out of free-text tag fields: "downloaded from …", "ripped using …", bare website names, and promo stuff in the Comment and Title fields. Modest by design — only the matched span is removed rather than the whole field, a Title is never emptied, and a field containing no junk is left completely alone. Can also wipe the Comment field outright instead. Every change is previewed first.
  - **Paste metadata** — a new **"Paste metadata…"** dialog (Manual Edit tab) takes a pasted blob and figures out what goes where. It auto-detects three shapes: a **tracklist** (numbered or `Artist - Title` lines, fuzzy-matched to your files by title/track), a **CSV/table** (one row per file, matched by a filename column or row order), and a **single "Field: value" block** (applied to every file). You can also **copy from a master file** — pull the album/album-artist/year/genre/disc/composer (and optionally the cover art) from one well-tagged file onto the rest. Every proposed change is shown in a reviewable, per-row checkable grid before anything is written.
- **Transfer metadata between sets of files** — copy all the tags (and cover art) from one folder of files onto another, even when the two folders are in different formats. Point it at a source folder whose files are well-tagged (say, a lower-quality rip with great metadata) and it auto-matches each of your loaded files to its counterpart by title/filename (extension-agnostic, fuzzy, with positional fallback when the counts line up), then copies only the fields you tick onto the better-quality copies. Perfect for moving metadata onto pristine but untagged files. Reachable from the grid right-click → **"Transfer metadata from another folder…"**, or via Paste metadata → **From another folder**; every change is reviewable before it's written, with optional per-file backups.
- **Write analysis to files** — bake what AudioAuditor measured back into the audio files themselves. A new grid right-click → **"Write Analysis to Files…"** dialog lets you pick any analyzed fields — LUFS, True Peak, Dynamic Range, the **real/measured bitrate**, sample rate, bit depth, MQA, AI verdict, fake-stereo, clipping, rip log, BPM, ReplayGain, and more — and writes each as a dedicated custom tag (ID3v2 `TXXX` for MP3, Vorbis comments for FLAC/OGG/Opus, `----` atoms for M4A/ALAC) plus a single human-readable `AudioAuditor:` summary line in the Comment field. Re-running replaces the summary instead of stacking it, your existing comment is preserved, and per-file backups are optional. (The measured bitrate is written as a tag/note — the *stated* bitrate lives in the audio header and can't be changed without re-encoding.)
- **"Wrapped" export** — the Wrapped stats dashboard can now be exported as a shareable **PNG**, **JPEG**, or one-page **PDF** (rendered with a solid background so it's never transparent/black), using a built-in dependency-free PDF writer.
- **Footer scan bar** — scanning now shows a compact progress bar with a live **ETA** read-out centered in the status footer, alongside a **pause/resume** button, so you can watch (and pause) a scan without it shoving the rest of the footer around.

- **CLI `credits` command** — `AudioAuditorCLI credits` (also available in interactive mode) prints the same open-source library/author/license/link info as the GUI's Credits window.
- **CLI `enrich` — live results & more sources** — the metadata enrich command now prints a per-file ✓/~/✗ line as each file finishes (with the matched source) instead of going quiet until the end, and lists any unmatched files at the end. New flags `--deezer`, `--theaudiodb`, `--discogs-token <t>`, and `--fanarttv-key <k>` (env vars `DISCOGS_TOKEN` / `FANARTTV_API_KEY` honored) enable the same extra sources and soundtrack handling as the GUI — the scanner engine is shared, so results match.
- **Color picker magnifier** — while the eyedropper is active and you hover over the album cover, a small color circle follows your cursor showing the exact color you're about to pick, making it much easier to land on the right shade. The magnifier now sits closer to the cursor and is a touch smaller for finer aiming.
- **CLI `selfcheck` command** — runs the built-in assertion suite against the build you actually have, covering the tag-comment merge, the junk detector, file renaming, and the AI scoring rules. Handy for confirming a portable download isn't corrupt.

### Improvements
- **ColorMatch colors are now saved per album cover** — picking or confirming a color scheme on one track automatically applies it to every track that has the *exact same* embedded cover, so whole albums theme consistently instead of needing the colors re-picked per file. Singles with their own cover stay independent, and any colors you'd already saved per-file migrate over automatically the first time you play the track.

#### AI detection — rebuilt scoring
- **Confidence now reflects evidence strength instead of marker count.** The detector always computed a strength-weighted score, but it was being discarded and re-derived from *how many* markers turned up. That inverted the whole hierarchy: one definitive **AudioSeal / SynthID / Suno** marker scored 65% and displayed as merely "Possible", while two vague generic phrases scored 100% and displayed as "Yes". A single verifiable marker now reads "Yes" on its own, and vague ones rank below it.
- **Detectors no longer cancel each other out.** Results used to be averaged, so a confirmed watermark sitting next to a barely-triggered spectral flag scored *lower* than the watermark alone. Independent evidence now reinforces instead of diluting — adding a signal can only raise the score.
- **Each detector counts for what it can actually prove** — a watermark or C2PA marker at full weight, the SH Labs model high, the spectral heuristics at half. The practical consequence: **the experimental spectral checks can no longer produce a confident "Yes" on their own**, only "Possible".
- **The AI column names its evidence** — `Yes - watermark (86%)` or `Possible - heuristic (52%)` instead of a bare percentage, so a heuristic guess never looks like proof. The same detail reaches the tooltip, the CLI, and the JSON output.
- **A confident "human" verdict from SH Labs now clears a spectral flag.** Its probability was previously only consulted when it happened to be incriminating, so the model could accuse a file but never exonerate one. It still never overrides an embedded watermark.
- **AI markers are found in the middle of files.** The raw-byte scan only ever read the first and last 64KB, so any marker written into the body of a file was invisible. It now covers 128KB at the start, 128KB at the end, and four 64KB slices spread through the body.
- **Heavily limited human masters are no longer flagged on their own.** The two obfuscation checks (hard limiting, crest-factor homogeneity) could satisfy the "two flags means suspicious" rule between themselves — and aggressive limiting is completely normal on loudness-war masters.
- **The AI tally in the status bar** counted only watermark hits, so it under-reported against the verdicts actually shown in the grid.

#### AI detection — a new spectral check, and a wider watermark scan

- **A new check looks for the one artifact that usually gets left behind.** The existing spectral checks all ask variations of "does this sound over-processed", which heavily-mastered human records also answer yes to. The new one asks something different: are there evenly spaced narrow ridges in the spectrum? That pattern comes from the upsampling layers inside the model itself — it's the audio equivalent of the checkerboard artifact in AI images — so it's tied to how the track was built rather than to how it was mixed. It also survives MP3 and AAC, unlike the ultrasonic check, which the encoder's lowpass usually erases.
  - Measured against a small labeled set (each lossless file also tested as a 320 kbps MP3 so the format couldn't be doing the separating): it fired on all three AI tracks and none of the human ones, and each transcode scored within 0.02 of its original.
  - **What this does not do yet:** a single check can't reach a verdict on its own — that rule is deliberate and unchanged — so a file where only this one fires still shows "No". The evidence now appears in the tooltip, the CLI, and the JSON output instead of being invisible. Turning it into a verdict on its own needs a much larger labeled set than six files, and that hasn't been done.
- **The spectral checks now look at the whole track instead of the first 30 seconds.** They read three ten-second regions from across the track, and — more importantly — each check now spreads its measurements over that whole span. Previously every check took its samples consecutively from the very beginning, which for most of them meant roughly the first three seconds. So checks asking "how much does this track vary" were answering about the intro and generalising to the song, and a sparse or slow-building opening read as machine-uniform. Measured effect: human tracks now score visibly further from AI ones than they did before, because real music varies across a track while a generator artifact stays put.
- **UTF-16 AI markers are detected now , for real this time.** An older version did do this, but the scan only ever decoded ASCII, so markers written in UTF-16 (common in ID3v2 frames and C2PA blobs) were invisible no matter how explicit they were. The scan now catches them regardless of encoding or byte alignment.
- **Six more generators recognised** — Riffusion, MusicGen, ElevenLabs Music, Mureka, CassetteAI, and Sonauto, plus `udio.ai`. Suno's model marker is matched by prefix rather than by listing `chirp-v2`, `v3`, `v4` one at a time, so new Suno models are covered the day they ship instead of whenever the list gets updated.
- **Google's Lyria is matched only in its qualified forms** ("Google Lyria", "DeepMind Lyria", `lyria-002`). Plain "lyria" is a perfectly plausible artist or track name, and a match there counts as evidence — one missed detection is a far better trade than wrongly accusing someone's song, which is the reason it was designed this way.
- **Spectral findings are kept even when they fall short of a verdict.** The checks' results were being computed and then thrown away unless they crossed the "suspicious" line, so a check that fired without corroboration left no trace anywhere. They're now recorded either way, and the AI column's tooltip names the specific checks that fired instead of showing only "Heuristic — spectral (52%)". This changes what you can see, not what the app concludes.

### Fixes
- **The automatic updater now fails if the SHA files do not match the offical source.** Update files must come from this project's official GitHub releases, downloads are size-limited, and SHA-256 sidecar files are parsed strictly. A missing or mismatched hash blocks installation instead of offering an unsafe "install anyway" option, and temporary updater files are isolated and cleaned up (NOTE: that there is a slight potential for this to misfire so if it alerts for you, please dont automatically think that the download has been compromised, I'm just trying to make this software safer for you guys :) ).
- **Custom service links can no longer launch arbitrary Windows protocols.** User-configured search links are restricted to absolute HTTP/HTTPS URLs. Maloja connections require HTTPS, with plain HTTP allowed only for a server running on the local machine.
- **Archive and playlist imports are hardened against malicious files.** Archive extraction now blocks path traversal and symbolic links, limits entry counts and extracted sizes, and removes partial extraction directories after rejection. Large or excessively long playlists are rejected before they can exhaust memory or processing time.
- **Unprotected Scrobbling API credentials are no longer left in plaintext settings.** Existing plaintext credential entries are migrated into the encrypted Windows credential file without overwriting newer encrypted values, credential saves are atomic, and plaintext entries are removed only after encrypted storage succeeds, this was not an issue for every key but was for some newer ones.
- **Single-instance file forwarding now rejects malformed IPC messages.** Invalid, oddly sized, or oversized payloads are discarded before unmanaged memory is read.
- **Fake lossless made from a 320 kbps source is caught now.** Take a 320 kbps MP3, convert it to FLAC, WAV, or AIFF, and AudioAuditor had mistakenly called it lossless. Two separate things had to be fixed. The verdict accepted any file whose content reached roughly 19.8 kHz — but LAME at 320 kbps stops at about 20.5 kHz, so the transcode sat comfortably inside the tolerance and passed. Underneath that, the cutoff scan usually never found the edge at all: it set its search threshold relative to the file's own midrange, so on any track with a quiet top end it looked thousands of Hz below the real edge, correctly rejected the wrong answer it had found, and then reported "full bandwidth" instead. Both are fixed — the scan now looks for the edge directly, and the verdict weighs **how steeply** the spectrum stops rather than only where. A mastering engineer's gentle rolloff and an encoder's brick wall land at similar frequencies but look nothing alike, and that difference is what the check now reads. Nothing that passed before can fail without a genuine wall being measured.
- **File details and exports now show "Cutoff Drop"** — the measured steepness of that spectral edge, in dB. This is the evidence behind a fake-lossless verdict, so it sits next to the verdict instead of staying buried in the analyzer. A blank or small value means the file simply ends gradually, which is normal.
- **Worth knowing:** this catches lowpassed sources, which is nearly all of them, but it is not magic. An encoder deliberately run with its lowpass disabled leaves content all the way to the top and no cutoff-based check — this one included — can tell that apart from real lossless. That needs a different kind of analysis entirely.
- **Wide-stereo tracks can't be falsely detected any more.** The spectrum was measured by folding the channels down to a mono mix first, and mixing cancels anything the two channels hold out of phase. On a track with wide stereo effects that could carve a gap into the spectrum that exists in neither channel on its own — and a gap near the top is exactly what now reads as a lossy source. Each channel is measured separately now, so a frequency counts as present if *any* channel has it.
- **DSD files (.dsf/.dff) now decode properly.** Much of the old decoder for this was wrong. The DSF header was read at the wrong offsets, so the sample rate was picked up from a field that is always zero. Both file types were unpacked as though each channel's data sat in one contiguous run, when DSF actually alternates fixed-size blocks between channels and DFF alternates single bytes — so stereo came out as noise. Anything above DSD64 was labelled with half its real rate, putting every frequency reading an octave out. And the conversion to PCM used no filtering, so the huge amount of ultrasonic noise that DSD deliberately parks above the audible range folded straight back down into the music. All four are fixed, and files are now decoded a piece at a time instead of being loaded into memory whole — a DSD128 track used to need gigabytes of RAM.
- **Mono Opus files are no longer reported as fake stereo.** The Opus reader assumed every file was stereo instead of reading the channel count from the file, so a mono track was decoded into two identical channels and then flagged "Mono Duplicate" by the fake-stereo detector — a problem the reader had created itself.
- **Correctly encoded MP2, AC-3, AMR and Speex files are no longer called fake.** The cutoff-versus-bitrate curves describe *encoders*, but they were being picked by *file extension*, and anything unrecognized fell back to LAME's MP3 curve. Those formats genuinely roll off lower than MP3 does at the same bitrate, so honest files failed a test meant for a different codec — a normal 192 kbps MP2 was reported as fake. Formats without a curve of their own now report "unknown" rather than accusing the file.
- **Audiobook and ringtone files are judged consistently.** `.m4b` and `.m4r` hold ordinary AAC, the same as `.m4a`, but they weren't recognized as AAC and were measured against the MP3 curve instead — so identical audio could get a different verdict depending only on the file's extension. `.mp4`, `.3gp`, `.3g2` and `.webm` were mismatched the same way.
- **Dynamic Range now agrees with other DR meters.** The measurement picked the 20% of the track with the widest gap between peak and average — which finds the quietest, most dynamic passages, like intros and fades. The established method uses the *loudest* 20%. Scores were coming out several points higher than foobar2000 or the standard DR meter would report for the same file.
- **Batch Editor scrollbars are now themed** — the scrollbars on the Rename tab, the Auto-Tag changes grid, the Manual Edit panel, and the "⚠ unmatched" failures popup now use the app's themed slider instead of the default Windows scrollbar, matching the rest of the window in both light and dark themes.
- **Batch Editor now matches the app theme** — every control in the Batch Editor (checkboxes, radio buttons, text boxes, dropdowns, tabs, buttons, and the changes/rename grids) is now fully themed, so nothing falls back to default Windows styling and no text blends into the background. Fixed several controls that referenced missing theme colors (invisible borders / wrong row colors) and verified readability across light and dark themes.
- **Open-source credits/licenses now work in the portable build** — the bundled license texts are embedded directly in the exe instead of shipping as loose files beside it, so "View license" in Settings → About → Open Source no longer fails on the portable build (it previously only worked in the installed version).
- **Tag clean-up no longer rewrites files that had no junk in them.** Any comment or title ending in punctuation ("Great song.") was reported as changed and written back, because trailing punctuation was being stripped even when no junk pattern had matched anything.
- **Tag clean-up no longer destroys dot-separated titles.** The bare-domain matcher treated ordinary English words as top-level domains, so `Back.To.Black` was cleaned down to `Black` and `Artist.Title.Live` was emptied entirely.
- **Transferring metadata between folders no longer applies bad matches.** When the two folders held different numbers of files, a barely-similar match was applied anyway and an unrelated file's tags were written over your own. Files without a close enough match are now left untouched and reported in the summary.
- **Writing analysis to files no longer adds foreign tag containers.** It created every tag type each format could hold, so MP3s came back with an APE tag attached and FLACs with both ID3v2 and APE — precisely the cruft the Metadata Strip tool exists to remove. Values now go into the tags a file already carries.
- **Renaming can change letter case again.** Every rename path in the app treated a case-only change as "nothing to do", so the Rename tab's case transform (lower / UPPER / Title) silently did nothing on Windows. All rename paths now share one implementation.
- **The Convert tab explains itself when FFmpeg is missing** — a link to a suitable build, the exact folder to drop the binary into, and a Re-check button, instead of a dead warning line that required restarting the app after installing it.
- **Batch edits reject a non-numeric year, disc, or track number** instead of quietly writing 0 and wiping that field across every selected file.
- **Covers fetched per album no longer collide** — grouping ignored the artist, so two different artists' "Greatest Hits" ended up sharing one piece of artwork.
- **Out-of-phase files record their stereo correlation.** Negative values were being skipped, which is exactly the case worth writing down.
- **Interactive CLI help lists `checklog` and `config`**, both of which already worked but were missing from the command list.

### Other
- **Supporters list** — added Jung and Pistachio to Settings → About → Supporters. (Support this project here through Github Sponsers or Ko-Fi, both attached in the repo, to get your name listed!)
- **Scan cache file is now smaller on disk** — cache entries no longer write empty strings or empty lists to JSON; fields that are not set for a given file are omitted entirely, reducing cache file size significantly for typical libraries.
- **Fewer settings that only had one sensible answer** — nine options were removed and the good behavior made unconditional. **Battery Saver** loses its "Entire program" checkbox and its five per-area checkboxes: turning it on now stops every ambient animation, with a single **Keep the audio visualizer running** option to spare the visualizer (the one exemption I think was actually used lol). If you were running per-area mode with the visualizer deliberately left animating, that choice is carried over automatically. **Cache album art colors** and **Persist color cache to disk** are gone — the color cache is always on and always saved, since turning it off only meant re-extracting the same palettes on every track change and every launch. **Use region-aware store URLs** is gone too; the separate **Store region** dropdown was always the real control, and switching it off just sent you to the wrong storefront. Existing settings files are read as-is, so nothing else is disturbed. Dont worry about these few settings being removed they wont effect anything and now that some of them are set to always on. It should be a large improvement for those who never went through the massive settings list to turn them on (I dont blame you guys, its pretty long lol), and they also are set to use very little storage in the kb to at most a few mb even for a large collection of info.
- **Scan cache is rebuilt once on first launch** — the AI scoring change and the fake-lossless fix both mean entries cached by an older version would carry stale results: the old AI confidence values, and lossless verdicts made before the spectral-edge check existed. They are recomputed rather than trusted.
- **Version identification** is now read from the assembly instead of four hardcoded strings that had drifted between 1.5 and 1.8.0.
- **The AI detection DSP now has regression tests** — the spectral maths had none, so thresholds could drift with nothing to catch it. There is now a test file covering the new grid-peak check (including the case most likely to produce false positives: an MP3's brick-wall lowpass, which must not be mistaken for a generator artifact) and the watermark scan's encoding handling. A separate, skipped-by-default harness prints raw measurements per file for re-tuning thresholds against your own labeled samples.
- **Removed dead code** — four superseded windows (the old standalone Now Playing window, Batch Metadata, Batch Rename, and the online/offline dialog) plus assorted unreferenced helpers were deleted; all of their functionality had already moved into the Batch Editor and the embedded Now Playing panel.

## v1.8.0

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
