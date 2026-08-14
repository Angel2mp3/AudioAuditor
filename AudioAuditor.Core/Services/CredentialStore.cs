using System.Text;

namespace AudioQualityChecker.Services;

/// <summary>
/// Reads and writes the encrypted credential file shared by every front-end.
/// </summary>
/// <remarks>
/// The file is <c>Documents/AudioAuditor/session.dat</c>: newline-separated <c>key=value</c>
/// pairs, encrypted as one blob by <see cref="SecretProtection.Current"/>. On Windows that is
/// DPAPI, which is exactly what the WPF build writes, so both builds read the same file.
///
/// Secrets must never go in <c>options.txt</c>. That file is plaintext, and WPF deletes any
/// credential keys it finds there on every launch — so anything written there is both exposed
/// and short-lived.
/// </remarks>
public static class CredentialStore
{
    /// <summary>Keys this store owns. WPF writes exactly these (ThemeManager.Persistence.cs:355).</summary>
    public static readonly string[] Keys =
    {
        "LastFmApiKey", "LastFmApiSecret", "LastFmSessionKey", "LastFmUsername",
        "LibreFmApiKey", "LibreFmApiSecret", "LibreFmSessionKey", "LibreFmUsername",
        "ListenBrainzUserToken", "ListenBrainzUsername",
        "MalojaServerUrl", "MalojaApiKey", "MalojaUsername",
        "DiscordRpcClientId", "AcoustIdApiKey", "DiscogsToken", "FanartTvApiKey",
        "SpotifyClientId", "SpotifyClientSecret", "YouTubeApiKey", "SHLabsCustomApiKey"
    };

    public static string FilePath => ResolvePath();

    private static string ResolvePath()
    {
        var overrideFile = Environment.GetEnvironmentVariable("AUDIOAUDITOR_SENSITIVE_FILE");
        if (!string.IsNullOrWhiteSpace(overrideFile))
        {
            try { return Path.GetFullPath(Environment.ExpandEnvironmentVariables(overrideFile)); }
            catch { /* unusable override; fall through to the default */ }
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "AudioAuditor",
            "session.dat");
    }

    /// <summary>
    /// Loads every stored credential. Returns an empty map when the file is missing or cannot
    /// be decrypted — a machine change or a different user account is not an error worth
    /// crashing over, it just means there is nothing to restore.
    /// </summary>
    public static Dictionary<string, string> Load()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            var path = FilePath;
            if (!File.Exists(path)) return values;

            var raw = File.ReadAllBytes(path);
            var decrypted = SecretProtection.Current.Unprotect(raw);

            string? content;
            if (decrypted != null)
            {
                content = Encoding.UTF8.GetString(decrypted);
            }
            else
            {
                // Pre-encryption files were plaintext. WPF still accepts them, so this must too,
                // or upgrading through the Avalonia build would silently lose the credentials.
                //
                // Undecryptable bytes are far more often an encrypted file from another machine
                // or user account than a legacy file, and those bytes will still contain a stray
                // '=' often enough to parse into nonsense "credentials". So the fallback only
                // applies when the content actually looks like the old key=value format.
                content = Encoding.UTF8.GetString(raw);
                if (!LooksLikeLegacyPlaintext(content)) return values;
            }

            foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = line.TrimEnd('\r').Split('=', 2);
                if (pair.Length == 2)
                    values[pair[0]] = pair[1];
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return values;
    }

    /// <summary>
    /// Whether <paramref name="content"/> is plausibly an old plaintext session file: printable
    /// text throughout, and every non-blank line in <c>key=value</c> form with a key that could
    /// be an identifier. Ciphertext and random bytes fail on the first control character.
    /// </summary>
    public static bool LooksLikeLegacyPlaintext(string content)
    {
        bool sawPair = false;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0) continue;

            int split = line.IndexOf('=');
            if (split <= 0) return false;

            var key = line[..split];
            foreach (var c in key)
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '.')
                    return false;

            // Values are opaque, but a control character means this was never text.
            foreach (var c in line.AsSpan(split + 1))
                if (char.IsControl(c))
                    return false;

            sawPair = true;
        }

        return sawPair;
    }

    /// <summary>
    /// Writes the given credentials, merging over whatever is already stored so a front-end
    /// that knows fewer keys cannot wipe the rest — the same rule as
    /// <see cref="OptionsFileStore"/>, learned the same way.
    /// </summary>
    public static void Save(IEnumerable<KeyValuePair<string, string>> values)
    {
        try
        {
            var merged = Load();
            foreach (var pair in values)
                merged[pair.Key] = pair.Value ?? "";

            // Keep WPF's key order so the decrypted bytes stay comparable between builds.
            var ordered = Keys.Where(merged.ContainsKey).Select(k => $"{k}={merged[k]}").ToList();
            ordered.AddRange(merged.Keys
                .Where(k => !Keys.Contains(k))
                .OrderBy(k => k, StringComparer.Ordinal)
                .Select(k => $"{k}={merged[k]}"));

            var plaintext = Encoding.UTF8.GetBytes(string.Join("\n", ordered));
            var encrypted = SecretProtection.Current.Protect(plaintext);

            var path = FilePath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            // Write via a temp file so an interrupted save cannot leave a half-written blob
            // that decrypts to nothing.
            var temp = path + ".tmp";
            File.WriteAllBytes(temp, encrypted);
            File.Move(temp, path, overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
