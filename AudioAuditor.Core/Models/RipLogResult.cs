using System.Collections.Generic;

namespace AudioQualityChecker.Models
{
    /// <summary>One scoring deduction (or note) from the rip-log evaluator.</summary>
    public sealed class RipLogDeduction
    {
        public string Message { get; init; } = "";
        /// <summary>cambia class: Critical, Bad, Neutral, Good, Perfect.</summary>
        public string Class { get; init; } = "Neutral";
        /// <summary>Raw unit score, e.g. "-20" or "0".</summary>
        public string Score { get; init; } = "";
    }

    /// <summary>
    /// Result of checking a CD ripping log (EAC / XLD / whipper) with the bundled cambia binary.
    /// Score is the OPS-style 0–100 evaluation; <see cref="Deductions"/> explains every point lost.
    /// </summary>
    public sealed class RipLogResult
    {
        /// <summary>True when cambia recognised and scored the log.</summary>
        public bool IsParsed { get; init; }

        /// <summary>False when the cambia binary couldn't be located.</summary>
        public bool BinaryAvailable { get; init; } = true;

        /// <summary>0–100 OPS score. -1 / unset when not parsed.</summary>
        public int Score { get; init; } = -1;

        /// <summary>Ripper name as reported by cambia, e.g. "Exact Audio Copy".</summary>
        public string Ripper { get; init; } = "";

        public string RipperVersion { get; init; } = "";
        public string Drive { get; init; } = "";

        /// <summary>Path to the log file that was checked.</summary>
        public string SourceFile { get; init; } = "";

        /// <summary>Human-readable reason when <see cref="IsParsed"/> is false.</summary>
        public string Error { get; init; } = "";

        public List<RipLogDeduction> Deductions { get; init; } = new();

        /// <summary>Short verdict bucket derived from the score.</summary>
        public string Verdict
        {
            get
            {
                if (!BinaryAvailable) return "N/A";
                if (!IsParsed) return "Unknown";
                if (Score >= 100) return "Perfect";
                if (Score >= 95) return "Good";
                if (Score >= 90) return "Suspect";
                return "Bad";
            }
        }

        /// <summary>Compact column text, e.g. "Good (97)" or "-".</summary>
        public string Display => IsParsed ? $"{Verdict} ({Score})" : "-";

        public static RipLogResult MissingBinary(string sourceFile = "") => new()
        {
            BinaryAvailable = false,
            IsParsed = false,
            SourceFile = sourceFile,
            Error = "cambia was not found. Bundle cambia(.exe) in third-party/cambia/ or install it on your PATH."
        };

        public static RipLogResult Unsupported(string sourceFile, string reason) => new()
        {
            IsParsed = false,
            SourceFile = sourceFile,
            Error = reason
        };
    }
}
