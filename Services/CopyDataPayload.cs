using System;
using System.Collections.Generic;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// Wire format for the file/folder paths a second instance forwards to the running one via
    /// WM_COPYDATA (see App.ForwardPathsToExistingInstance and MainWindow.WndProc).
    ///
    /// Both sides used to compute the UTF-16 byte count independently, and disagreed: the sender
    /// counted the string's NUL terminator in cbData (correct — Marshal.StringToHGlobalUni writes
    /// it), while the receiver passed cbData/2 straight to Marshal.PtrToStringUni, which copies
    /// exactly that many chars WITHOUT stopping at the NUL. The last path therefore arrived with a
    /// trailing '\0', and File.Exists/Directory.Exists reject a path containing one — so the last
    /// (usually only) file of every "Open With" was dropped in silence.
    ///
    /// Keeping both directions in one place is what stops that from drifting apart again.
    /// </summary>
    internal static class CopyDataPayload
    {
        // Paths cannot contain '\n' on Windows, so it is a safe record separator.
        private const char Separator = '\n';
        private const int MaxByteCount = 1024 * 1024;

        /// <summary>
        /// Builds the string to marshal and the cbData byte count to advertise. The count includes
        /// the NUL terminator that <c>Marshal.StringToHGlobalUni</c> appends, which is what the
        /// receiving side expects <see cref="Unpack"/> to account for.
        /// </summary>
        internal static (string Payload, int ByteCount) Pack(IEnumerable<string> paths)
        {
            string payload = string.Join(Separator, paths ?? Array.Empty<string>());
            return (payload, (payload.Length + 1) * sizeof(char));
        }

        /// <summary>
        /// Number of chars to ask <c>Marshal.PtrToStringUni</c> for, given an advertised cbData.
        /// One less than cbData/2 because the terminator must not be copied into the string.
        /// </summary>
        internal static int CharCountFor(int byteCount) => Math.Max(0, byteCount / sizeof(char) - 1);

        internal static bool IsValidByteCount(int byteCount) =>
            byteCount > 0 && byteCount <= MaxByteCount && byteCount % sizeof(char) == 0;

        /// <summary>
        /// Splits a received payload back into paths. Tolerates a trailing NUL (and any stray
        /// interior ones) so a sender that advertises the older, off-by-one count still works —
        /// the two sides live in different processes and can be different builds mid-upgrade.
        /// </summary>
        internal static string[] Unpack(string? payload)
        {
            if (string.IsNullOrEmpty(payload)) return Array.Empty<string>();

            var parts = payload.Split(Separator, StringSplitOptions.RemoveEmptyEntries);
            var result = new List<string>(parts.Length);
            foreach (var part in parts)
            {
                var cleaned = part.Trim('\0');
                if (!string.IsNullOrWhiteSpace(cleaned)) result.Add(cleaned);
            }
            return result.ToArray();
        }
    }
}
