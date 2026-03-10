# Changelog

## v1.3.0

## Annocements

- **Website:** Official site launched at [audioauditor.org](https://audioauditor.org)
- **Looking for beta testers** - I can only test so much on my own, anyone who would like to be a tester let me know. In general though please report any bugs you may incounter and if you'd like new features, please open a Github issue :)

### New Features
- **Custom CPU & Memory Limits** — In addition to preset modes (Auto/Low/Medium/High/Maximum), set your own thread count and memory cap
- **ALAC File Status** - When alac files were used it wouldnt load or would be detected as just a normal "M4A" file, now correctly loads and notes that its an ALAC file next to the M4A extension.

### Improvements
- **Fixed ALAC Files** - ALAC files were reported to be not loading/all being read as "Unkown" this should be fixed now. 
- **Fixed CLI Tool** the last version was outdated that didnt include the newer bittrate detection and status imrpovements, now the same as the GUI
- **Discord Rich Presence Fixes** — Now fixed and more comprehensieve / easier to set up the rich presence in your discord dev settings.
- Fixed shuffle button as it used to get stuck on the old theme when changed to the new one, now correctly updates to the new theme when changed.
- Replaced old/wrong music service icons with their offical ones
- CPU and memory performance limits are now enforced during analysis and spectrogram batch export
- Single-instance enforcement — launching the app again brings the existing window to front
- Fixed smaller general bugs
- Slight preformance boost