using System;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// Boolean formatting for the shared <c>options.txt</c>.
    ///
    /// The WPF, Avalonia and CLI builds all read and write the same file, so a boolean written by
    /// one build has to parse in the others. WPF interpolates <see cref="bool"/> directly, giving
    /// <c>True</c>/<c>False</c>, and reads with <c>bool.TryParse</c> — which rejects <c>1</c>.
    /// The Avalonia build historically wrote <c>1</c>/<c>0</c> and compared against <c>"1"</c>,
    /// so each build silently reset the other's settings.
    ///
    /// <see cref="Format"/> is the WPF spelling, which is the one every build now writes.
    /// <see cref="Parse"/> is deliberately tolerant of both spellings: users have <c>1</c>/<c>0</c>
    /// values sitting in their file today from earlier Avalonia runs, and rejecting those would
    /// trade one silent reset for another.
    /// </summary>
    public static class OptionsBool
    {
        /// <summary>Formats a value the way the WPF build writes it.</summary>
        public static string Format(bool value) => value ? "True" : "False";

        /// <summary>
        /// Parses a value written by any build. Accepts <c>True</c>/<c>False</c> in any casing and
        /// <c>1</c>/<c>0</c>. Anything else — empty, null, or garbage — yields
        /// <paramref name="fallback"/>, so a key the caller defaults to <c>true</c>
        /// (<c>AutoPlayNext</c>) is not silently switched off by an unreadable line.
        /// </summary>
        public static bool Parse(string? value, bool fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;

            var trimmed = value.Trim();
            if (bool.TryParse(trimmed, out var parsed)) return parsed;
            if (trimmed == "1") return true;
            if (trimmed == "0") return false;
            return fallback;
        }
    }
}
