using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace AudioQualityChecker.Services;

/// <summary>What happened to a rename request.</summary>
public enum RenameOutcome
{
    /// <summary>The file was moved to the requested path.</summary>
    Renamed,

    /// <summary>Source and target are the same path, or the source is missing — nothing to do.</summary>
    Unchanged,

    /// <summary>A different file already owns the target name; nothing was overwritten.</summary>
    TargetExists
}

/// <summary>What happened to a restore-from-backup request.</summary>
public enum RestoreOutcome
{
    /// <summary>The backup was written back over the original path. The backup is still on disk.</summary>
    Restored,

    /// <summary>The named backup does not exist.</summary>
    BackupMissing,

    /// <summary>The backup is zero bytes — a partial copy from a failed backup, not a usable original.</summary>
    BackupEmpty
}

/// <summary>
/// Shared on-disk file operations for audio files: renaming, pre-write backups, and restoring
/// from them. Every rename path in the app (Batch Editor, the two MainWindow rename tools, and the
/// CLI) goes through here so they share the same collision rules — and so that a change that only
/// alters letter case actually happens. The backup helper lives here for the same reason: it was
/// previously copied into five separate services, one of which had drifted and would throw on a
/// repeat backup.
/// </summary>
public static class FileRenamer
{
    /// <summary>
    /// Marks a sibling file as a pre-write backup. The timestamp that follows is
    /// <c>yyyyMMddHHmmss</c> UTC, which sorts chronologically under an ordinal string compare —
    /// that is what lets <see cref="FindBackups"/> order them without parsing.
    /// </summary>
    public const string BackupSuffix = ".audioauditor-backup-";

    /// <summary>
    /// Copies <paramref name="filePath"/> to a timestamped <c>.audioauditor-backup-*</c> sibling
    /// before a destructive tag write. Backing the same file up twice within one second is a no-op
    /// rather than an error, so callers can invoke this freely per file — the copy already on disk
    /// is the older, and therefore more original, state.
    /// </summary>
    public static void CreateBackup(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;
        string backup = filePath + BackupSuffix + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        if (!File.Exists(backup))
            CopyAtomic(filePath, backup);
    }

    /// <summary>
    /// The backups that exist for <paramref name="filePath"/>, oldest first — so the first entry is
    /// the closest thing to the untouched original. Returns empty when the folder is unreadable.
    /// </summary>
    public static IReadOnlyList<string> FindBackups(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return Array.Empty<string>();

        string? dir = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return Array.Empty<string>();

        try
        {
            // Match on the full name so "song.flac" never picks up "song.flac.mp3"'s backups.
            string prefix = Path.GetFileName(filePath) + BackupSuffix;
            var found = Directory.EnumerateFiles(dir, prefix + "*")
                .Where(p => Path.GetFileName(p).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();
            found.Sort(StringComparer.OrdinalIgnoreCase);
            return found;
        }
        catch (IOException) { return Array.Empty<string>(); }
        catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    /// <summary>
    /// Writes <paramref name="backupPath"/> back over <paramref name="targetPath"/>, replacing
    /// whatever is there. The backup is a whole-file copy, so this recovers the audio, every tag,
    /// and the embedded art in one step.
    ///
    /// The backup is deliberately left on disk: restoring the wrong one is a mistake the user must
    /// be able to walk back. Throws on I/O errors so callers can count them.
    /// </summary>
    public static RestoreOutcome Restore(string backupPath, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath)) return RestoreOutcome.BackupMissing;
        if (string.IsNullOrWhiteSpace(targetPath)) return RestoreOutcome.BackupMissing;

        // A zero-byte backup is what a disk-full CreateBackup used to leave behind. Restoring it
        // would destroy the file it was supposed to protect.
        if (new FileInfo(backupPath).Length == 0) return RestoreOutcome.BackupEmpty;

        // The original may have been renamed or moved away since the backup was taken, in which case
        // its folder can be gone too.
        string? targetDir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);

        CopyAtomic(backupPath, targetPath, overwrite: true);
        return RestoreOutcome.Restored;
    }

    /// <summary>
    /// Copy that never leaves a half-written file at <paramref name="destination"/>.
    ///
    /// A plain <see cref="File.Copy(string, string, bool)"/> that runs out of disk throws partway
    /// and leaves the truncated remains behind — which then looks like a valid backup to
    /// <see cref="CreateBackup"/>'s existence check, and like a valid original to a restore.
    /// Building the copy under a temp name and moving it into place makes the destination appear
    /// only once it is complete. Same shape as <c>CredentialStore</c>'s write.
    /// </summary>
    private static void CopyAtomic(string source, string destination, bool overwrite = false)
    {
        string temp = destination + ".aa-tmp-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            File.Copy(source, temp, overwrite: false);
            File.Move(temp, destination, overwrite);
        }
        catch
        {
            try { File.Delete(temp); } catch { /* the original error is the one worth reporting */ }
            throw;
        }
    }

    /// <summary>
    /// Moves <paramref name="from"/> to <paramref name="to"/>, creating the target directory when
    /// needed. Never overwrites an unrelated file. Throws on I/O errors so callers can count them.
    /// </summary>
    public static RenameOutcome Rename(string from, string to)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to)) return RenameOutcome.Unchanged;
        if (!File.Exists(from)) return RenameOutcome.Unchanged;
        if (string.Equals(from, to, StringComparison.Ordinal)) return RenameOutcome.Unchanged;

        // Windows paths are case-insensitive, so a target that differs only by case is this same
        // file — not a collision. Every call site used to conflate the two and silently skip,
        // which is why the Rename tab's case transform (lower / UPPER / Title) did nothing.
        bool caseOnly = string.Equals(from, to, StringComparison.OrdinalIgnoreCase);

        if (!caseOnly && File.Exists(to)) return RenameOutcome.TargetExists;

        string? targetDir = Path.GetDirectoryName(to);
        if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);

        if (!caseOnly)
        {
            File.Move(from, to);
            return RenameOutcome.Renamed;
        }

        // A direct case-only Move is a no-op (or throws) on a case-insensitive filesystem, so bounce
        // through a temp name in the same directory to force the rename through.
        string temp = from + ".aa-rename-" + Guid.NewGuid().ToString("N")[..8];
        File.Move(from, temp);
        try
        {
            File.Move(temp, to);
        }
        catch
        {
            // Put it back rather than leave the file orphaned under the temp name.
            try { File.Move(temp, from); } catch { /* nothing better to do; rethrow the real error */ }
            throw;
        }
        return RenameOutcome.Renamed;
    }

    /// <summary>Assert-based check for the case-only branch (no test framework in this repo).</summary>
    public static void SelfCheck()
    {
        string dir = Path.Combine(Path.GetTempPath(), "aa-selfcheck-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            string lower = Path.Combine(dir, "song.mp3");
            string upper = Path.Combine(dir, "SONG.mp3");
            File.WriteAllText(lower, "x");

            Assert(Rename(lower, upper) == RenameOutcome.Renamed, "case-only rename should report Renamed");
            Assert(Path.GetFileName(Directory.GetFiles(dir)[0]) == "SONG.mp3", "case-only rename should land on disk");

            Assert(Rename(upper, upper) == RenameOutcome.Unchanged, "same path should be Unchanged");

            string other = Path.Combine(dir, "other.mp3");
            File.WriteAllText(other, "y");
            Assert(Rename(other, upper) == RenameOutcome.TargetExists, "collision should be TargetExists");
            Assert(File.ReadAllText(upper) == "x", "collision must not overwrite");

            string moved = Path.Combine(dir, "sub", "moved.mp3");
            Assert(Rename(other, moved) == RenameOutcome.Renamed, "rename into a new folder should work");
            Assert(File.Exists(moved), "target directory should have been created");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }

        static void Assert(bool ok, string what)
        {
            if (!ok) throw new Exception("FileRenamer.SelfCheck failed: " + what);
        }
    }
}
