using System;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// "time left" text for the analysis progress bar, estimated from the average time per
    /// file so far. Pure formatting, shared so the WPF and Avalonia progress bars read the same.
    /// </summary>
    public static class AnalysisEta
    {
        /// <summary>
        /// Returns the label, or an empty string when there is nothing meaningful to show
        /// (nothing finished yet, so no average; or the batch is already done).
        /// </summary>
        public static string Format(int completed, int total, TimeSpan elapsed)
        {
            if (completed < 1 || completed >= total) return "";

            double avgPerFile = elapsed.TotalSeconds / completed;
            double etaSeconds = avgPerFile * (total - completed);

            if (etaSeconds < 1) return "< 1s";
            if (etaSeconds < 60) return $"~{(int)etaSeconds}s left";

            int mins = (int)(etaSeconds / 60);
            int secs = (int)(etaSeconds % 60);
            return secs > 0 ? $"~{mins}m {secs}s left" : $"~{mins}m left";
        }
    }
}
