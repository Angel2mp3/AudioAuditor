using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Media;
using Windows.Storage.Streams;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// Integrates with Windows SystemMediaTransportControls (SMTC) so media overlays
    /// like FluentFlyout, EarTrumpet, and the Win+G bar show now-playing info.
    /// </summary>
    public class SmtcService : IDisposable
    {
        private SystemMediaTransportControls? _smtc;
        private bool _disposed;
        private static readonly Guid _sessionGuid = Guid.NewGuid();

        public event EventHandler? PlayRequested;
        public event EventHandler? PauseRequested;
        public event EventHandler? NextRequested;
        public event EventHandler? PreviousRequested;

        // COM interop to get SMTC for a Win32 window handle
        [DllImport("api-ms-win-media-sysmt-l1-1-0.dll", EntryPoint = "GetForWindow",
            ExactSpelling = true, PreserveSig = false)]
        private static extern void GetSmtcForWindow(IntPtr appWindow,
            [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object smtc);

        public bool Initialize(IntPtr hwnd)
        {
            try
            {
                Guid guid = typeof(SystemMediaTransportControls).GUID;
                GetSmtcForWindow(hwnd, ref guid, out object result);
                _smtc = (SystemMediaTransportControls)result;

                _smtc.IsEnabled = true;
                _smtc.IsPlayEnabled = true;
                _smtc.IsPauseEnabled = true;
                _smtc.IsNextEnabled = true;
                _smtc.IsPreviousEnabled = true;
                _smtc.IsStopEnabled = true;

                _smtc.ButtonPressed += Smtc_ButtonPressed;
                return true;
            }
            catch
            {
                _smtc = null;
                return false;
            }
        }

        private void Smtc_ButtonPressed(SystemMediaTransportControls sender,
            SystemMediaTransportControlsButtonPressedEventArgs args)
        {
            switch (args.Button)
            {
                case SystemMediaTransportControlsButton.Play:
                    PlayRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case SystemMediaTransportControlsButton.Pause:
                    PauseRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case SystemMediaTransportControlsButton.Next:
                    NextRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case SystemMediaTransportControlsButton.Previous:
                    PreviousRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }

        public void UpdatePlaybackState(bool isPlaying, bool isPaused)
        {
            if (_smtc == null) return;
            if (isPlaying)
                _smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
            else if (isPaused)
                _smtc.PlaybackStatus = MediaPlaybackStatus.Paused;
            else
                _smtc.PlaybackStatus = MediaPlaybackStatus.Stopped;
        }

        public async Task UpdateNowPlaying(string? artist, string? title, string? albumCoverPath)
        {
            if (_smtc == null) return;

            var updater = _smtc.DisplayUpdater;
            updater.Type = MediaPlaybackType.Music;
            updater.MusicProperties.Title = title ?? "Unknown Title";
            updater.MusicProperties.Artist = artist ?? "Unknown Artist";

            // Set album art thumbnail if available
            if (!string.IsNullOrEmpty(albumCoverPath) && File.Exists(albumCoverPath))
            {
                try
                {
                    var storageFile = await global::Windows.Storage.StorageFile.GetFileFromPathAsync(albumCoverPath);
                    updater.Thumbnail = RandomAccessStreamReference.CreateFromFile(storageFile);
                }
                catch { /* Thumbnail is optional */ }
            }
            else
            {
                updater.Thumbnail = null;
            }

            updater.Update();
        }

        public void UpdateNowPlayingFromTags(string filePath)
        {
            if (_smtc == null) return;

            string? artist = null;
            string? title = null;
            string? coverPath = null;

            try
            {
                using var tagFile = TagLib.File.Create(filePath);
                artist = tagFile.Tag.FirstPerformer;
                title = tagFile.Tag.Title;

                // Extract embedded album art to temp file for SMTC
                if (tagFile.Tag.Pictures?.Length > 0)
                {
                    var pic = tagFile.Tag.Pictures[0];
                    // Per-session GUID filename to avoid predictable temp file names
                    coverPath = Path.Combine(Path.GetTempPath(), $"audioauditor_smtc_cover_{_sessionGuid}.jpg");
                    File.WriteAllBytes(coverPath, pic.Data.Data);
                }
            }
            catch { }

            if (string.IsNullOrWhiteSpace(title))
                title = Path.GetFileNameWithoutExtension(filePath);

            _ = UpdateNowPlaying(artist, title, coverPath);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_smtc != null)
            {
                _smtc.IsEnabled = false;
                _smtc.ButtonPressed -= Smtc_ButtonPressed;
            }

            // Clean up the per-session cover temp file on dispose
            try
            {
                var coverPath = Path.Combine(Path.GetTempPath(), $"audioauditor_smtc_cover_{_sessionGuid}.jpg");
                if (File.Exists(coverPath))
                    File.Delete(coverPath);
            }
            catch { }
        }
    }
}
