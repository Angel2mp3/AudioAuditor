using AudioQualityChecker.Models;

namespace AudioQualityChecker.Services
{
    public static partial class ThemeManager
    {
        /// <summary>Snapshots the current NP layout into a named profile.</summary>
        public static NpLayoutProfile CaptureNpLayout(string name) =>
            NpLayoutProfileCapture.Capture(name);

        /// <summary>Applies a saved profile's values back onto the NP layout properties.</summary>
        public static void ApplyNpLayout(NpLayoutProfile profile)
        {
            NpLayoutProfileCapture.Apply(profile);
            SavePlayOptions();
        }
    }
}
