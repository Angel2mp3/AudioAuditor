using System;
using System.Collections.Generic;
using System.Linq;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// Decides which files in a batch get an SH Labs scan when the remaining quota does not
    /// cover all of them.
    ///
    /// Cached results are free — they cost no quota — so they are always included. Uncached
    /// files are taken in order until the quota runs out.
    /// </summary>
    public static class SHLabsBatchPlanner
    {
        /// <summary>
        /// Outcome of planning a batch. <see cref="Targets"/> is null when SH Labs is off for
        /// this batch entirely (no quota left); it is never null-but-needed.
        /// </summary>
        public sealed record Plan(
            HashSet<string>? Targets,
            int UncachedCount,
            int Available)
        {
            /// <summary>True when the batch needs more scans than the quota allows.</summary>
            public bool IsPartial => Targets != null && UncachedCount > Available;

            /// <summary>True when there is no quota left at all, so nothing will be scanned.</summary>
            public bool IsExhausted => Targets == null;
        }

        /// <summary>
        /// Plans <paramref name="paths"/> against <paramref name="available"/> remaining scans.
        /// <paramref name="isCached"/> reports whether a path already has a cached verdict.
        /// </summary>
        public static Plan Create(IReadOnlyList<string> paths, int available, Func<string, bool> isCached)
        {
            var uncached = paths.Where(p => !isCached(p)).ToList();

            if (available <= 0)
                return new Plan(Targets: null, uncached.Count, Math.Max(0, available));

            if (uncached.Count <= available)
                return new Plan(new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase),
                    uncached.Count, available);

            // Over quota: as many uncached files as the quota covers, plus every cached file,
            // since looking those up costs nothing.
            var targets = new HashSet<string>(uncached.Take(available), StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths.Where(isCached))
                targets.Add(path);

            return new Plan(targets, uncached.Count, available);
        }

        /// <summary>Message shown when the batch is larger than the remaining quota.</summary>
        public static string PartialMessage(Plan plan) =>
            $"You have {plan.Available} SH Labs scan{(plan.Available == 1 ? "" : "s")} remaining today. " +
            $"{plan.UncachedCount} file{(plan.UncachedCount == 1 ? "" : "s")} need scanning.\n\n" +
            $"The first {plan.Available} file{(plan.Available == 1 ? "" : "s")} will be scanned with SH Labs. " +
            "The rest will use your other selected detection methods.\n\nContinue?";

        /// <summary>Message shown when there is no quota left.</summary>
        public const string ExhaustedMessage =
            "You've reached your SH Labs scan limit. Files will be analyzed using your other selected detection methods.";
    }
}
