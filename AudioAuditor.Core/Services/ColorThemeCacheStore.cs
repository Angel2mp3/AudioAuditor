using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudioQualityChecker.Services;

public sealed record ColorThemeCacheEntry(
    string Key,
    AlbumColorExtractor.DominantColors Colors);

public sealed record ColorThemeManualPickEntry(
    string Key,
    IReadOnlyList<AlbumColorExtractor.Color> Colors);

public sealed record ColorThemeCacheData(
    IReadOnlyList<ColorThemeCacheEntry> Entries,
    IReadOnlyList<ColorThemeManualPickEntry> ManualPicks);

/// <summary>
/// Serializes and parses the NP color cache file.
///
/// Schema v3 (current): compact, no whitespace. Colors stored as 6-char uppercase hex strings.
/// Short field names: k=key, p=primary, s=secondary, t=tertiary, b=background, x=textOnBackground.
/// Manual picks: k=key, c=colors (hex string array).
/// ~70% smaller on disk vs. the v2 indented-object format.
///
/// Schema v2 (legacy): indented, colors as {r,g,b} objects, long field names.
/// Both formats are read; only v3 is written.
/// </summary>
public static class ColorThemeCacheStore
{
    public const int CurrentSchemaVersion = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static ColorThemeCacheData Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Empty();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            int schemaVersion = root.TryGetProperty("schemaVersion", out var schema)
                && schema.ValueKind == JsonValueKind.Number
                ? schema.GetInt32() : 0;

            // Accept v2 and v3; anything else discards auto-entries but preserves manual picks.
            bool loadAutoEntries = schemaVersion >= 2 && schemaVersion <= CurrentSchemaVersion;

            var entries = loadAutoEntries ? ReadEntries(root, schemaVersion) : [];
            var manualPicks = ReadManualPicks(root);
            return new ColorThemeCacheData(entries, manualPicks);
        }
        catch
        {
            return Empty();
        }
    }

    public static string Serialize(
        IEnumerable<ColorThemeCacheEntry> entries,
        IEnumerable<ColorThemeManualPickEntry> manualPicks)
    {
        var document = new CacheDocumentDto
        {
            SchemaVersion = CurrentSchemaVersion,
            Entries = entries
                .Where(e => !string.IsNullOrWhiteSpace(e.Key))
                .Select(e => new CacheEntryDto
                {
                    K = e.Key,
                    P = ToHex(e.Colors.Primary),
                    S = ToHex(e.Colors.Secondary),
                    T = ToHex(e.Colors.Tertiary),
                    B = ToHex(e.Colors.Background),
                    X = ToHex(e.Colors.TextOnBackground)
                })
                .ToList(),
            ManualPicks = manualPicks
                .Where(p => !string.IsNullOrWhiteSpace(p.Key))
                .Select(p => new ManualPickDto
                {
                    K = p.Key,
                    C = p.Colors.Take(6).Select(ToHex).ToList()
                })
                .Where(p => p.C.Count > 0)
                .ToList()
        };

        return JsonSerializer.Serialize(document, JsonOptions);
    }

    private static ColorThemeCacheData Empty() => new([], []);

    private static List<ColorThemeCacheEntry> ReadEntries(JsonElement root, int schemaVersion)
    {
        var entries = new List<ColorThemeCacheEntry>();
        if (!root.TryGetProperty("entries", out var entriesEl)
            || entriesEl.ValueKind != JsonValueKind.Array)
            return entries;

        foreach (var entry in entriesEl.EnumerateArray())
        {
            // Support both v3 short key "k" and v2 long key "key"
            string key = entry.TryGetProperty("k", out var kEl) ? kEl.GetString() ?? ""
                       : entry.TryGetProperty("key", out var keyEl) ? keyEl.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(key)) continue;

            AlbumColorExtractor.Color primary, secondary, tertiary, background, text;

            if (schemaVersion >= 3)
            {
                if (!entry.TryGetProperty("p", out var pEl)) continue;
                primary    = ParseHex(pEl.GetString());
                secondary  = entry.TryGetProperty("s", out var sEl) ? ParseHex(sEl.GetString()) : primary;
                tertiary   = entry.TryGetProperty("t", out var tEl) ? ParseHex(tEl.GetString()) : secondary;
                background = entry.TryGetProperty("b", out var bEl) ? ParseHex(bEl.GetString()) : default;
                text       = entry.TryGetProperty("x", out var xEl) ? ParseHex(xEl.GetString()) : new AlbumColorExtractor.Color(240, 240, 240);
            }
            else
            {
                // v2 legacy: colors as {r,g,b} objects with long field names
                if (!entry.TryGetProperty("primary", out var primaryEl)) continue;
                if (!entry.TryGetProperty("secondary", out var secondaryEl)) continue;
                if (!entry.TryGetProperty("background", out var backgroundEl)) continue;

                primary    = ParseColorObj(primaryEl);
                secondary  = ParseColorObj(secondaryEl);
                tertiary   = entry.TryGetProperty("tertiary", out var tertiaryEl) ? ParseColorObj(tertiaryEl) : secondary;
                background = ParseColorObj(backgroundEl);
                text       = entry.TryGetProperty("textOnBackground", out var textEl)
                             ? ParseColorObj(textEl)
                             : new AlbumColorExtractor.Color(240, 240, 240);
            }

            entries.Add(new ColorThemeCacheEntry(
                key,
                new AlbumColorExtractor.DominantColors(primary, secondary, tertiary, background, text)));
        }

        return entries;
    }

    private static List<ColorThemeManualPickEntry> ReadManualPicks(JsonElement root)
    {
        var manualPicks = new List<ColorThemeManualPickEntry>();
        if (!root.TryGetProperty("manualPicks", out var picksEl)
            || picksEl.ValueKind != JsonValueKind.Array)
            return manualPicks;

        foreach (var pickEntry in picksEl.EnumerateArray())
        {
            string key = pickEntry.TryGetProperty("k", out var kEl) ? kEl.GetString() ?? ""
                       : pickEntry.TryGetProperty("key", out var keyEl) ? keyEl.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(key)) continue;

            var colors = new List<AlbumColorExtractor.Color>(6);

            // v3: "c" array of hex strings; v2: "colors" array of {r,g,b} objects
            if (pickEntry.TryGetProperty("c", out var cEl) && cEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var colorEl in cEl.EnumerateArray())
                {
                    if (colors.Count >= 6) break;
                    colors.Add(ParseHex(colorEl.GetString()));
                }
            }
            else if (pickEntry.TryGetProperty("colors", out var colorsEl) && colorsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var colorEl in colorsEl.EnumerateArray())
                {
                    if (colors.Count >= 6) break;
                    colors.Add(ParseColorObj(colorEl));
                }
            }

            if (colors.Count > 0)
                manualPicks.Add(new ColorThemeManualPickEntry(key, colors));
        }

        return manualPicks;
    }

    private static AlbumColorExtractor.Color ParseColorObj(JsonElement el) =>
        new((byte)el.GetProperty("r").GetInt32(),
            (byte)el.GetProperty("g").GetInt32(),
            (byte)el.GetProperty("b").GetInt32());

    private static string ToHex(AlbumColorExtractor.Color c) => $"{c.R:X2}{c.G:X2}{c.B:X2}";

    private static AlbumColorExtractor.Color ParseHex(string? hex)
    {
        if (hex?.Length == 6)
        {
            try
            {
                return new AlbumColorExtractor.Color(
                    Convert.ToByte(hex[0..2], 16),
                    Convert.ToByte(hex[2..4], 16),
                    Convert.ToByte(hex[4..6], 16));
            }
            catch { }
        }
        return new AlbumColorExtractor.Color(128, 128, 128);
    }

    private sealed class CacheDocumentDto
    {
        public int SchemaVersion { get; set; }
        public List<CacheEntryDto> Entries { get; set; } = [];
        public List<ManualPickDto> ManualPicks { get; set; } = [];
    }

    private sealed class CacheEntryDto
    {
        [JsonPropertyName("k")] public string K { get; set; } = "";
        [JsonPropertyName("p")] public string P { get; set; } = "";
        [JsonPropertyName("s")] public string S { get; set; } = "";
        [JsonPropertyName("t")] public string? T { get; set; }
        [JsonPropertyName("b")] public string B { get; set; } = "";
        [JsonPropertyName("x")] public string? X { get; set; }
    }

    private sealed class ManualPickDto
    {
        [JsonPropertyName("k")] public string K { get; set; } = "";
        [JsonPropertyName("c")] public List<string> C { get; set; } = [];
    }
}
