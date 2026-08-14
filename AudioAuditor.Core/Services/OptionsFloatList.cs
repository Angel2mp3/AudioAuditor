using System;
using System.Globalization;
using System.Linq;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// The <c>;</c>-joined float lists in the shared <c>options.txt</c> — <c>EqualizerGains</c>
    /// being the one that exists today.
    ///
    /// Both halves must be <see cref="CultureInfo.InvariantCulture"/> or the file stops being
    /// shareable: on a comma-decimal locale a current-culture write emits <c>1,5</c>, which an
    /// invariant read then rejects (the band silently resets to 0), and a current-culture read of
    /// an invariant <c>1.5</c> yields 15 — a 10x gain jump on a real EQ band. The asymmetry is
    /// invisible on en-US, which is why it needs a helper rather than a convention.
    /// </summary>
    public static class OptionsFloatList
    {
        /// <summary>Formats a list the way the WPF build writes it: one decimal, invariant.</summary>
        public static string Format(System.Collections.Generic.IReadOnlyList<float> values)
        {
            return string.Join(";", values.Select(v => v.ToString("F1", CultureInfo.InvariantCulture)));
        }

        /// <summary>
        /// Reads a <c>;</c>-joined list into <paramref name="destination"/>, up to its length.
        /// Entries that don't parse leave that slot's current value alone, so a single corrupt
        /// field can't wipe the whole band set.
        /// </summary>
        public static void ParseInto(string? value, float[] destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (string.IsNullOrWhiteSpace(value)) return;

            var parts = value.Split(';');
            int count = Math.Min(parts.Length, destination.Length);
            for (int i = 0; i < count; i++)
                if (float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                    destination[i] = parsed;
        }
    }
}
