using System;
using System.Windows.Media;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// Process-wide rendering path. Auto lets WPF use the GPU (Default); ForceSoftware
    /// pins the CPU (software) rasterizer for machines with broken/blacklisted drivers.
    /// Applied once at startup (see App.xaml.cs). There is intentionally no "force GPU"
    /// value — WPF has no per-process API to force hardware on, only to force it off.
    /// </summary>
    public enum GpuRenderMode { Auto, ForceSoftware }

    public static partial class ThemeManager
    {
        // ─── Reduce Motion ───
        // Accessibility-facing alias for the legacy AnimationsEnabled flag. Reduce
        // Motion ON == animations OFF. Kept as an alias (not a new field) so the
        // existing persistence key and ~60 internal AnimationsEnabled call sites
        // stay untouched.
        public static bool ReduceMotion
        {
            get => !AnimationsEnabled;
            set => AnimationsEnabled = !value;
        }

        // ─── Battery Saver ───
        // Manual perf mode. The master toggle gates everything; EntireProgram (the
        // default when enabled) suppresses every animated area, otherwise the
        // per-area flags decide. All consulted through AnimationPolicy, never raw.
        public static bool BatterySaverEnabled { get; set; }
        public static bool BatterySaverEntireProgram { get; set; } = true;
        public static bool BatterySaverNpBackground { get; set; } = true;
        public static bool BatterySaverVisualizer { get; set; } = true;
        public static bool BatterySaverCoverGlow { get; set; } = true;
        public static bool BatterySaverLyrics { get; set; } = true;
        public static bool BatterySaverPlaybar { get; set; } = true;

        // Explicit override: keep the visualizer animating even when Battery Saver would
        // otherwise suppress it (including "Entire Program" mode). Honored by AnimationPolicy.
        public static bool BatterySaverKeepVisualizer { get; set; }

        // ─── GPU acceleration ───
        public static GpuRenderMode GpuRenderMode { get; set; } = GpuRenderMode.Auto;

        public static GpuRenderMode ParseGpuRenderMode(string? value) =>
            Enum.TryParse<GpuRenderMode>(value, ignoreCase: true, out var mode)
                ? mode
                : GpuRenderMode.Auto;

        /// <summary>
        /// Hardware rendering tier reported by WPF (0 = software, 1 = partial, 2 = full GPU).
        /// Used only for the Settings read-out. Cheap; safe to call after startup.
        /// </summary>
        public static int GetRenderTier() => RenderCapability.Tier >> 16;
    }
}
