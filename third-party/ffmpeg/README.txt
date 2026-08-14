Bundled FFmpeg
==============

Drop an FFmpeg executable into this folder (or install ffmpeg on your PATH) to enable
two things:

  1. The Batch Editor's "Convert" tab, which converts audio between formats.
  2. Decoding for analysis. FFmpeg is the last step of the decoder chain, so it is what
     makes AAC/M4A, ALAC, WMA, APE, WavPack, TAK, Musepack, Speex and AC-3 analyzable.
     On Linux and macOS this matters most: Media Foundation does not exist there, so
     without ffmpeg those formats are read for metadata but never scanned. Formats with
     a built-in managed decoder (FLAC, WAV, AIFF, OGG, Opus, MP3/MP2, DSD) never touch
     ffmpeg and work regardless.

    third-party/ffmpeg/ffmpeg.exe   (Windows)
    third-party/ffmpeg/ffmpeg       (Linux/macOS)

On Linux/macOS the simplest route is the system package: `sudo apt install ffmpeg`
or `brew install ffmpeg`. Run `audioauditorcli --version` to see whether it was found.

IMPORTANT — license:
  Use an **LGPL** build of FFmpeg (not a GPL build) to keep AudioAuditor's own code
  under Apache-2.0. AudioAuditor only ever runs ffmpeg as a separate process and never
  links against it, so bundling the LGPL build does not affect this project's license.

  Start at the official download page: https://ffmpeg.org/download.html — it links every
  platform's builds. For Windows, pick a build whose name contains "lgpl" (the Windows
  packagers listed there ship both GPL and LGPL variants).

The build is copied next to the app at compile time (see AudioQualityChecker.csproj).
At runtime, AudioConversionService.FindFfmpeg() looks for the executable here, then
next to the app, then on the system PATH. If none is found, the Convert tab shows a
notice and conversion is disabled, and the formats listed under (2) above report
"install ffmpeg for full format support" instead of a quality verdict — everything
else still works.

See Third.Party.Notices/ffmpeg-LICENSE.txt for the attribution and source offer.
