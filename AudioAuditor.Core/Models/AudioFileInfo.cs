using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace AudioQualityChecker.Models
{
    public enum AudioStatus
    {
        Analyzing,
        Valid,
        Fake,
        Unknown,
        Corrupt,
        Optimized
    }

    public class AudioFileInfo : INotifyPropertyChanged
    {
        private AudioStatus _status = AudioStatus.Analyzing;

        public AudioStatus Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public string Artist { get; set; } = "";
        public string Title { get; set; } = "";
        public string Album { get; set; } = "";
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public string FolderPath { get; set; } = "";
        public int SampleRate { get; set; }
        public int BitsPerSample { get; set; }
        public string Duration { get; set; } = "";
        public double DurationSeconds { get; set; }
        public string FileSize { get; set; } = "";
        public long FileSizeBytes { get; set; }
        public int ReportedBitrate { get; set; }
        public int ActualBitrate { get; set; }
        public int EstimatedSourceBitrate { get; set; }
        public string Extension { get; set; } = "";
        public int EffectiveFrequency { get; set; }
        /// <summary>
        /// How steeply the spectrum falls across <see cref="EffectiveFrequency"/>, in dB.
        /// 0 means no discrete cutoff was found. A large value in a lossless container is the
        /// signature of an encoder lowpass that survived the conversion.
        /// </summary>
        public double CutoffDropDb { get; set; }
        /// <summary>
        /// Sample rate the spectrum was actually measured at, which is the decoder's output rate
        /// and not always <see cref="SampleRate"/> (that one is what the container declares).
        /// DSD declares its 1-bit rate but decodes to PCM; Opus always decodes at 48 kHz. Comparing
        /// <see cref="EffectiveFrequency"/> against the wrong Nyquist is meaningless, so anything
        /// judging the cutoff must use this. 0 means unmeasured — fall back to SampleRate.
        /// </summary>
        public int AnalysisSampleRate { get; set; }
        public int Channels { get; set; }

        // File dates
        public DateTime DateModified { get; set; }
        public DateTime DateCreated { get; set; }

        // Clipping detection
        public bool HasClipping { get; set; }
        public double ClippingPercentage { get; set; }
        public long ClippingSamples { get; set; }
        public double MaxSampleLevel { get; set; } // 0.0 to 1.0 peak
        public double MaxSampleLevelDb { get; set; } // peak in dB
        public bool HasScaledClipping { get; set; } // clipping at reduced level
        public double ScaledClippingPercentage { get; set; }

        // BPM, Replay Gain, Frequency
        public int Bpm { get; set; }

        private double _replayGain;
        public double ReplayGain
        {
            get => _replayGain;
            set { _replayGain = value; OnPropertyChanged(); OnPropertyChanged(nameof(ReplayGainDisplay)); }
        }

        private bool _hasReplayGain;
        public bool HasReplayGain
        {
            get => _hasReplayGain;
            set { _hasReplayGain = value; OnPropertyChanged(); OnPropertyChanged(nameof(ReplayGainDisplay)); }
        }

        // NOT a dominant/fundamental frequency despite the name — AudioAnalyzer assigns the
        // container's sample rate here, so this duplicates SampleRate. Left in place because the
        // scan cache persists it, but nothing should present it as a pitch measurement.
        public int Frequency { get; set; }

        // Error info for corrupt files
        public string ErrorMessage { get; set; } = "";

        // MQA detection
        public bool IsMqa { get; set; }
        public bool IsMqaStudio { get; set; }
        public string MqaOriginalSampleRate { get; set; } = "";
        public string MqaEncoder { get; set; } = "";

        // AI detection
        public bool IsAiGenerated { get; set; }
        public string AiSource { get; set; } = "";
        public List<string> AiSources { get; set; } = new();

        /// <summary>
        /// Marker-strength-weighted confidence (0–1) from <c>AiWatermarkDetector</c>. Strong,
        /// named-service or watermark markers score far higher than generic phrases, so this is
        /// not the same as "how many markers were found".
        /// </summary>
        public double AiConfidence { get; set; }

        // Experimental AI detection (spectral analysis)
        public bool ExperimentalAiSuspicious { get; set; }
        public double ExperimentalAiConfidence { get; set; }
        public List<string> ExperimentalAiFlags { get; set; } = new();

        // SH Labs AI detection (API-based)
        public bool SHLabsScanned { get; set; }
        public string SHLabsPrediction { get; set; } = ""; // "Human Made", "Pure AI", "Processed AI"
        public double SHLabsProbability { get; set; }       // 0–100
        public double SHLabsConfidence { get; set; }        // 0–100
        public string SHLabsAiType { get; set; } = "";

        // Album cover
        public bool HasAlbumCover { get; set; }

        // ALAC codec detected inside M4A container
        public bool IsAlac { get; set; }

        // Silence detection
        public double LeadingSilenceMs { get; set; }
        public double TrailingSilenceMs { get; set; }
        public int MidTrackSilenceGaps { get; set; } // number of gaps ≥ 500ms
        public double TotalMidSilenceMs { get; set; }
        public bool HasExcessiveSilence { get; set; }

        // Dynamic Range
        public double DynamicRange { get; set; } // DR score (dB), 0 = not calculated
        public bool HasDynamicRange { get; set; }

        // Fake stereo detection
        public bool IsFakeStereo { get; set; }
        public string FakeStereoType { get; set; } = ""; // "Mono Duplicate", "Artificially Widened", ""
        // Pearson coefficient, -1.0–1.0 (1.0 = identical channels, negative = out of phase).
        // 0.0 also means "not measured" — there is no separate Has* flag for this one.
        public double StereoCorrelation { get; set; }

        // Cue sheet virtual track
        public bool IsCueVirtualTrack { get; set; }
        public string CueSheetPath { get; set; } = "";
        public int CueTrackNumber { get; set; }
        public TimeSpan CueStartTime { get; set; }
        public TimeSpan CueEndTime { get; set; } // Zero = end of file

        // True Peak (inter-sample, dBTP)
        public double TruePeakDbTP { get; set; } // e.g. -0.3 dBTP
        public bool HasTruePeak { get; set; }

        // Integrated LUFS (EBU R128)
        public double IntegratedLufs { get; set; } // e.g. -14.0 LUFS
        public bool HasLufs { get; set; }

        // CD Rip Checker — verdict for the rip log found next to this file (cambia-scored)
        public int RipLogScore { get; set; } = -1; // 0–100 OPS score; -1 = no log
        public string RipLogVerdict { get; set; } = ""; // "Perfect", "Good", "Suspect", "Bad"
        public bool HasRipLog { get; set; }

        // Display properties
        public string DateModifiedDisplay => DateModified != default ? DateModified.ToString("yyyy-MM-dd HH:mm") : "-";
        public string DateCreatedDisplay => DateCreated != default ? DateCreated.ToString("yyyy-MM-dd HH:mm") : "-";
        public string FormatDisplay => IsAlac ? $"{Extension} (ALAC)" : Extension;
        public string SampleRateDisplay => SampleRate > 0 ? $"{SampleRate:N0} Hz" : "-";
        public string BitsPerSampleDisplay => BitsPerSample > 0 ? $"{BitsPerSample}-bit" : "-";
        public string ReportedBitrateDisplay => ReportedBitrate > 0 ? $"{ReportedBitrate} kbps" : "-";
        public string ActualBitrateDisplay => ActualBitrate > 0 ? $"{ActualBitrate} kbps" : "-";
        public string EffectiveFrequencyDisplay => EffectiveFrequency > 0 ? $"{EffectiveFrequency:N0} Hz" : "-";
        public string ChannelsDisplay => Channels > 0 ? (Channels == 1 ? "Mono" : Channels == 2 ? "Stereo" : $"{Channels}ch") : "-";
        public string ClippingDisplay
        {
            get
            {
                if (HasClipping) return $"YES ({ClippingPercentage:F2}%)";
                if (HasScaledClipping) return $"SCALED ({MaxSampleLevelDb:F1} dB, {ScaledClippingPercentage:F2}%)";
                return "No";
            }
        }
        public string BpmDisplay => Bpm > 0 ? $"{Bpm}" : "-";
        public string ReplayGainDisplay => HasReplayGain ? $"{ReplayGain:+0.00;-0.00;0.00} dB" : "-";
        public string FrequencyDisplay => Frequency > 0 ? $"{Frequency:N0} Hz" : "-";
        public string MqaDisplay => IsMqa ? (IsMqaStudio ? $"MQA Studio ({MqaOriginalSampleRate})" : $"MQA ({MqaOriginalSampleRate})") : "No";
        public string SilenceDisplay
        {
            get
            {
                if (!HasExcessiveSilence && LeadingSilenceMs < 1000 && TrailingSilenceMs < 1000 && MidTrackSilenceGaps == 0)
                    return "OK";
                var parts = new List<string>();
                if (LeadingSilenceMs >= 1000) parts.Add($"Lead: {FormatMs(LeadingSilenceMs)}");
                if (TrailingSilenceMs >= 1000) parts.Add($"Trail: {FormatMs(TrailingSilenceMs)}");
                if (MidTrackSilenceGaps > 0) parts.Add($"{MidTrackSilenceGaps} gap{(MidTrackSilenceGaps > 1 ? "s" : "")} ({FormatMs(TotalMidSilenceMs)})");
                return parts.Count > 0 ? string.Join(" | ", parts) : "OK";
            }
        }
        public string FakeStereoDisplay => IsFakeStereo ? FakeStereoType : "No";
        public string DynamicRangeDisplay => HasDynamicRange ? $"DR-{DynamicRange:F0}" : "-";
        public string TruePeakDisplay => HasTruePeak ? $"{TruePeakDbTP:F1} dBTP" : "-";
        public string LufsDisplay => HasLufs ? $"{IntegratedLufs:F1} LUFS" : "-";
        public string RipLogDisplay => HasRipLog ? $"{RipLogVerdict} ({RipLogScore})" : "-";

        /// <summary>
        /// Tooltip clarifying that rip accuracy is judged from the ripper's LOG (via cambia), never
        /// inferred from the audio itself — so "no log" means "not verified", not "bad rip".
        /// </summary>
        public string RipLogTooltip => HasRipLog
            ? $"Rip log scored {RipLogVerdict} ({RipLogScore}/100) by cambia, read from the EAC / XLD / whipper log."
            : "No rip log found in this file's folder. Rip accuracy can only be verified from the ripper's log — it is never judged from the audio itself.";

        private static string FormatMs(double ms)
        {
            if (ms >= 60000) return $"{ms / 60000:F1}m";
            if (ms >= 1000) return $"{ms / 1000:F1}s";
            return $"{(int)ms}ms";
        }
        /// <summary>Three-state AI verdict: "Yes" (≥70% confidence), "Possible" (35–70%), or "No" (&lt;35% or no detector flagged).</summary>
        public string AiVerdict
        {
            get
            {
                if (!IsAnyAiDetected) return "No";
                double conf = AiCombinedConfidence;
                if (conf >= 70.0) return "Yes";
                if (conf >= 35.0) return "Possible";
                return "No";
            }
        }

        // How far each detector is allowed to move the verdict. These sources are not equally
        // trustworthy and must not be pooled as if they were: a watermark is verifiable evidence,
        // the SH Labs model is a trained-but-opaque second opinion, and the spectral checks are
        // proxies for "sounds over-processed" — which heavily-limited human masters also trip.
        private const double WatermarkWeight = 1.0;
        private const double ShLabsWeight = 0.8;
        private const double SpectralWeight = 0.5;

        /// <summary>
        /// Combined confidence (0–100) that this file is AI generated.
        ///
        /// Detectors are combined with noisy-OR (<c>1 - Π(1 - pᵢ)</c>) rather than averaged.
        /// Averaging let weak evidence *drag down* strong evidence — a confirmed watermark next to
        /// a barely-triggered spectral flag scored lower than the watermark alone. Independent
        /// evidence should reinforce, so adding a signal can now only raise the score.
        ///
        /// Because the spectral detector is capped at <see cref="SpectralWeight"/>, heuristics on
        /// their own can reach "Possible" but never a confident "Yes". Only verifiable evidence or
        /// the trained model can accuse a file outright. <see cref="SelfCheck"/> pins that down.
        /// </summary>
        public double AiCombinedConfidence
        {
            get
            {
                // SH Labs reports probability_ai_generated directly, so a low value is real
                // evidence the file is human — not a reason to ignore the result, which is what
                // the old `Prediction != "Human Made"` string gate did.
                bool shLabsExonerates = SHLabsScanned
                    && SHLabsProbability < 35.0
                    && SHLabsConfidence >= 70.0;

                double notAi = 1.0;
                if (IsAiGenerated)
                {
                    // Fall back to the reporting threshold for entries cached before AiConfidence
                    // existed, so a stale row degrades to "weakly detected" rather than to zero.
                    double conf = AiConfidence > 0 ? AiConfidence : 0.5;
                    notAi *= 1.0 - Clamp01(conf * WatermarkWeight);
                }
                if (SHLabsScanned)
                    notAi *= 1.0 - Clamp01(SHLabsProbability / 100.0 * ShLabsWeight);

                // A model that actually listened to the file outranks a proxy heuristic, so when it
                // confidently says "human" the spectral term is dropped rather than fudged down.
                // Hard watermark evidence is deliberately left untouched by this.
                if (ExperimentalAiSuspicious && !shLabsExonerates)
                    notAi *= 1.0 - Clamp01(ExperimentalAiConfidence * SpectralWeight);

                return (1.0 - notAi) * 100.0;
            }
        }

        private static double Clamp01(double value) => value < 0 ? 0 : value > 1 ? 1 : value;

        /// <summary>
        /// True when the verdict rests on verifiable evidence — an embedded watermark, a named
        /// generator tag, or a C2PA manifest — rather than on heuristics alone. Worth surfacing:
        /// a spectral guess and a cryptographic watermark used to render identically.
        /// </summary>
        public bool HasVerifiableAiEvidence => IsAiGenerated;

        /// <summary>Which tier of evidence the verdict rests on: "watermark", "model", or "heuristic".</summary>
        public string AiEvidenceKind =>
            HasVerifiableAiEvidence ? "watermark"
            : (SHLabsScanned && SHLabsProbability >= 35.0) ? "model"
            : ExperimentalAiSuspicious ? "heuristic"
            : "";

        /// <summary>
        /// Single-line display: "Yes - watermark (86%)" / "Possible - heuristic (52%)" / "No".
        /// Naming the evidence tier keeps a heuristic guess from reading like proof, and still fits
        /// one grid line.
        /// </summary>
        public string AiDisplay
        {
            get
            {
                string verdict = AiVerdict;
                if (verdict == "No") return "No";
                string kind = AiEvidenceKind;
                string tier = kind.Length > 0 ? $" - {kind}" : "";
                return $"{verdict}{tier} ({AiCombinedConfidence:F0}%)";
            }
        }

        /// <summary>True when AI verdict is Yes or Possible — used to drive row highlighting.</summary>
        public bool IsAiPossibleOrYes => AiVerdict != "No";

        /// <summary>Compact confidence percentage for secondary display (kept for CLI/JSON use).</summary>
        public string AiConfidenceDisplay
        {
            get
            {
                if (!IsAnyAiDetected) return "";
                return $"{AiCombinedConfidence:F0}%";
            }
        }

        /// <summary>Detailed tooltip showing breakdown from each detection method.</summary>
        public string AiDetailTooltip
        {
            get
            {
                var parts = new List<string>();
                if (IsAiGenerated)
                    parts.Add($"Verified marker: {AiSource} ({AiConfidence:P0})");
                if (ExperimentalAiSuspicious)
                    parts.Add($"Heuristic — spectral ({ExperimentalAiConfidence:P0})");

                // Name the checks that fired. "Heuristic — spectral (52%)" tells a user nothing they
                // can act on or argue with; "Spectral grid peaks (Δf=118 Hz)" does. This tooltip is
                // the only place the WPF grid surfaces spectral evidence at all.
                if (ExperimentalAiFlags.Count > 0)
                    parts.Add($"Spectral checks: {string.Join(", ", ExperimentalAiFlags)}");
                if (SHLabsScanned && SHLabsPrediction != "Human Made")
                {
                    string label = !string.IsNullOrEmpty(SHLabsAiType)
                        ? $"SH Labs: {SHLabsPrediction} — {SHLabsAiType} ({SHLabsProbability:F0}%)"
                        : $"SH Labs: {SHLabsPrediction} ({SHLabsProbability:F0}%)";
                    parts.Add(label);
                }
                else if (SHLabsScanned)
                {
                    parts.Add("SH Labs: Human Made");
                }
                return parts.Count > 0 ? string.Join(" + ", parts) : "No AI detected";
            }
        }

        /// <summary>
        /// True when ANY AI detector flagged this file (watermark, spectral, or SH Labs). The
        /// SH Labs arm keys off the reported probability rather than the prediction string, so an
        /// unexpected or renamed label from the API can't silently suppress a high-probability hit.
        /// </summary>
        public bool IsAnyAiDetected =>
            IsAiGenerated
            || ExperimentalAiSuspicious
            || (SHLabsScanned && SHLabsProbability >= 35.0);

        // Favorites
        private bool _isFavorite;
        public bool IsFavorite
        {
            get => _isFavorite;
            set { _isFavorite = value; OnPropertyChanged(); }
        }

        private int _favoriteOrder;
        public int FavoriteOrder
        {
            get => _favoriteOrder;
            set { _favoriteOrder = value; OnPropertyChanged(); }
        }

        // Identity / user-state fields that a re-analysis must never overwrite — copying these
        // would change which row this is, or wipe the favorite star / cue association.
        private static readonly HashSet<string> NonAnalysisFields = new(StringComparer.Ordinal)
        {
            nameof(FilePath), nameof(FileName), nameof(FolderPath), nameof(Extension),
            nameof(IsFavorite), nameof(FavoriteOrder),
            nameof(IsCueVirtualTrack), nameof(CueSheetPath), nameof(CueTrackNumber),
            nameof(CueStartTime), nameof(CueEndTime),
        };

        // Writable, non-identity scalar properties — the set a refresh may copy. Cached once.
        private static readonly PropertyInfo[] CopyableAnalysisProperties =
            typeof(AudioFileInfo)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0
                            && !NonAnalysisFields.Contains(p.Name))
                .ToArray();

        /// <summary>
        /// Copies freshly re-analyzed values from <paramref name="other"/> into this row in place
        /// (so favorites, selection, sort, grouping and row identity are preserved). When
        /// <paramref name="onlyFields"/> is null every analysis field is copied; otherwise only the
        /// named properties are — used to fill a single newly-enabled column without touching the
        /// rest. Raises a blanket PropertyChanged so the grid's computed display columns refresh.
        /// </summary>
        public void CopyAnalysisFrom(AudioFileInfo other, IReadOnlyCollection<string>? onlyFields = null)
        {
            if (other == null) return;

            foreach (var prop in CopyableAnalysisProperties)
            {
                if (onlyFields != null && !onlyFields.Contains(prop.Name))
                    continue;
                prop.SetValue(this, prop.GetValue(other));
            }

            // Empty/null name tells WPF "all bindings on this object may have changed", which
            // refreshes the display-only columns (BpmDisplay, MqaDisplay, …) that don't notify.
            OnPropertyChanged(string.Empty);
        }

        /// <summary>Stamps the CD Rip Checker verdict for this row and refreshes its grid cell.</summary>
        public void SetRipLog(int score, string verdict)
        {
            RipLogScore = score;
            RipLogVerdict = verdict ?? "";
            HasRipLog = true;
            OnPropertyChanged(nameof(RipLogScore));
            OnPropertyChanged(nameof(RipLogVerdict));
            OnPropertyChanged(nameof(HasRipLog));
            OnPropertyChanged(nameof(RipLogDisplay));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
