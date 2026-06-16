using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudioQualityChecker.Models
{
    /// <summary>
    /// A named, user-saved Now Playing layout. Captures the COMPLETE layout: every per-context
    /// size/offset bundle (windowed/fullscreen × visualizer off/on) plus glow and backdrop
    /// settings. The layout values are stored as a property-name → string-value map (the same
    /// keys ThemeManager persists), so adding a new layout property doesn't require changing this
    /// model — only ThemeManager's captured-key list. Selecting a profile restores the entire look.
    /// </summary>
    public sealed class NpLayoutProfile
    {
        public string Name { get; set; } = "";

        /// <summary>Layout property name → invariant string value. Populated by ThemeManager.</summary>
        public Dictionary<string, string> Values { get; set; } = new();

        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public NpLayoutProfile Clone() => new()
        {
            Name = Name,
            Values = new Dictionary<string, string>(Values, StringComparer.OrdinalIgnoreCase),
        };
    }
}
