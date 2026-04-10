using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace AudioQualityChecker.Services;

/// <summary>
/// Simple translation service using MyMemory free API (no key required for basic use).
/// </summary>
public static class TranslateService
{
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    // Supported language pairs for the UI (sorted by name for display)
    public static readonly Dictionary<string, string> LanguageNames = new()
    {
        ["af"] = "Afrikaans",
        ["sq"] = "Albanian",
        ["ar"] = "Arabic",
        ["hy"] = "Armenian",
        ["az"] = "Azerbaijani",
        ["eu"] = "Basque",
        ["be"] = "Belarusian",
        ["bn"] = "Bengali",
        ["bs"] = "Bosnian",
        ["bg"] = "Bulgarian",
        ["ca"] = "Catalan",
        ["zh"] = "Chinese (Simplified)",
        ["zh-TW"] = "Chinese (Traditional)",
        ["hr"] = "Croatian",
        ["cs"] = "Czech",
        ["da"] = "Danish",
        ["nl"] = "Dutch",
        ["en"] = "English",
        ["et"] = "Estonian",
        ["fi"] = "Finnish",
        ["fr"] = "French",
        ["gl"] = "Galician",
        ["ka"] = "Georgian",
        ["de"] = "German",
        ["el"] = "Greek",
        ["gu"] = "Gujarati",
        ["ht"] = "Haitian Creole",
        ["he"] = "Hebrew",
        ["hi"] = "Hindi",
        ["hu"] = "Hungarian",
        ["is"] = "Icelandic",
        ["id"] = "Indonesian",
        ["ga"] = "Irish",
        ["it"] = "Italian",
        ["ja"] = "Japanese",
        ["kn"] = "Kannada",
        ["kk"] = "Kazakh",
        ["ko"] = "Korean",
        ["lv"] = "Latvian",
        ["lt"] = "Lithuanian",
        ["mk"] = "Macedonian",
        ["ms"] = "Malay",
        ["ml"] = "Malayalam",
        ["mt"] = "Maltese",
        ["mr"] = "Marathi",
        ["mn"] = "Mongolian",
        ["no"] = "Norwegian",
        ["fa"] = "Persian",
        ["pl"] = "Polish",
        ["pt"] = "Portuguese",
        ["pa"] = "Punjabi",
        ["ro"] = "Romanian",
        ["ru"] = "Russian",
        ["sr"] = "Serbian",
        ["sk"] = "Slovak",
        ["sl"] = "Slovenian",
        ["es"] = "Spanish",
        ["sw"] = "Swahili",
        ["sv"] = "Swedish",
        ["tl"] = "Tagalog",
        ["ta"] = "Tamil",
        ["te"] = "Telugu",
        ["th"] = "Thai",
        ["tr"] = "Turkish",
        ["uk"] = "Ukrainian",
        ["ur"] = "Urdu",
        ["vi"] = "Vietnamese",
        ["cy"] = "Welsh",
    };

    /// <summary>
    /// Translate a single line of text.
    /// </summary>
    public static async Task<string> TranslateAsync(string text, string fromLang, string toLang)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        try
        {
            string langPair = $"{fromLang}|{toLang}";
            string url = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(text)}&langpair={Uri.EscapeDataString(langPair)}";

            var response = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);

            if (doc.RootElement.TryGetProperty("responseData", out var data)
                && data.TryGetProperty("translatedText", out var translated))
            {
                var result = translated.GetString();
                if (!string.IsNullOrWhiteSpace(result)
                    && !result.Equals("PLEASE SELECT TWO LANGUAGES", StringComparison.OrdinalIgnoreCase)
                    && !result.Equals(text, StringComparison.OrdinalIgnoreCase))
                {
                    return result;
                }
            }
        }
        catch { /* fall through */ }

        return text;
    }

    /// <summary>
    /// Translate multiple lines, batching where possible.
    /// Returns a parallel list of translated strings.
    /// </summary>
    public static async Task<List<string>> TranslateLinesAsync(
        IReadOnlyList<string> lines, string fromLang, string toLang)
    {
        var results = new List<string>(lines.Count);

        // Batch into groups of ~5 lines joined by newline (API limit ~500 chars)
        var batch = new List<string>();
        var batchIndices = new List<(int start, int count)>();

        int current = 0;
        while (current < lines.Count)
        {
            batch.Clear();
            int batchStart = current;
            int charCount = 0;

            while (current < lines.Count && charCount + lines[current].Length < 450 && batch.Count < 5)
            {
                batch.Add(lines[current]);
                charCount += lines[current].Length;
                current++;
            }

            if (batch.Count == 0 && current < lines.Count)
            {
                // Single very long line
                batch.Add(lines[current]);
                current++;
            }

            string joined = string.Join("\n", batch);
            string translated = await TranslateAsync(joined, fromLang, toLang);
            var parts = translated.Split('\n');

            for (int i = 0; i < batch.Count; i++)
            {
                results.Add(i < parts.Length ? parts[i].Trim() : batch[i]);
            }
        }

        return results;
    }

    /// <summary>
    /// Simple language detection using MyMemory.
    /// Returns a 2-letter ISO code or "en" as fallback.
    /// </summary>
    public static async Task<string> DetectLanguageAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "en";

        try
        {
            // Use a dummy translation to English and check the detected source
            string url = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(text)}&langpair=autodetect|en";
            var response = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);

            if (doc.RootElement.TryGetProperty("responseData", out var data)
                && data.TryGetProperty("detectedLanguage", out var detected))
            {
                var lang = detected.GetString();
                if (!string.IsNullOrWhiteSpace(lang) && lang.Length >= 2)
                    return lang[..2].ToLowerInvariant();
            }
        }
        catch { }

        return "en";
    }
}
