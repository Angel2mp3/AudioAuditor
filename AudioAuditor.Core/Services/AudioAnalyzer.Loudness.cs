using System;

namespace AudioQualityChecker.Services
{
    public static partial class AudioAnalyzer
    {        // ═══════════════════════════════════════════════════════
        //  True Peak Measurement (4x oversampled inter-sample peaks)
        // ═══════════════════════════════════════════════════════

        private static readonly Lazy<double[][]> _oversamplingPhases = new(ComputeOversamplingPhases);
        private static double[][] GetOversamplingPhases() => _oversamplingPhases.Value;

        private static double[][] ComputeOversamplingPhases()
        {
            // Precomputed 4x oversampling lowpass filter (12-tap sinc * Kaiser window)
            // Phase 0 = original sample (identity), phases 1-3 = interpolated positions
            const int taps = 12;
            var phases = new double[4][];
            for (int p = 0; p < 4; p++)
            {
                phases[p] = new double[taps];
                double sum = 0;
                for (int k = 0; k < taps; k++)
                {
                    double n = k - (taps - 1) / 2.0 + p / 4.0;
                    double sinc = Math.Abs(n) < 1e-10 ? 1.0 : Math.Sin(Math.PI * n) / (Math.PI * n);
                    // Kaiser window (beta=6)
                    double x = 2.0 * k / (taps - 1) - 1.0;
                    double kaiser = BesselI0(6.0 * Math.Sqrt(Math.Max(0, 1.0 - x * x))) / BesselI0(6.0);
                    phases[p][k] = sinc * kaiser;
                    sum += phases[p][k];
                }
                // Normalize so filter has unity gain
                if (Math.Abs(sum) > 1e-10)
                    for (int k = 0; k < taps; k++)
                        phases[p][k] /= sum;
            }
            return phases;
        }

        private static double BesselI0(double x)
        {
            double sum = 1.0, term = 1.0;
            for (int k = 1; k <= 20; k++)
            {
                term *= (x / (2.0 * k)) * (x / (2.0 * k));
                sum += term;
                if (term < 1e-12 * sum) break;
            }
            return sum;
        }

        // ═══════════════════════════════════════════════════════
        //  Integrated LUFS (ITU-R BS.1770 / EBU R128 simplified)
        // ═══════════════════════════════════════════════════════

        private struct BiquadCoefficients
        {
            public double b0, b1, b2, a1, a2;
        }

        private struct BiquadState
        {
            public double z1, z2;
        }

        private static double ApplyBiquad(ref BiquadState state, BiquadCoefficients c, double input)
        {
            double output = c.b0 * input + state.z1;
            state.z1 = c.b1 * input - c.a1 * output + state.z2;
            state.z2 = c.b2 * input - c.a2 * output;
            return output;
        }

        private static void GetKWeightingCoefficients(int sampleRate,
            out BiquadCoefficients preFilter, out BiquadCoefficients rlbFilter)
        {
            // ITU-R BS.1770-4 K-weighting filter coefficients
            // Pre-filter: high shelf (+4 dB above ~1.5 kHz)
            // RLB filter: high-pass (−3 dB at ~38 Hz)
            // These are exact for 48 kHz; we use bilinear transform scaling for other rates.
            double sr = sampleRate;

            // Pre-filter (shelf boost for head-related transfer function)
            {
                double f0 = 1681.974450955533;
                double G = 3.999843853973347; // dB
                double Q = 0.7071752369554196;
                double K = Math.Tan(Math.PI * f0 / sr);
                double Vh = Math.Pow(10.0, G / 20.0);
                double Vb = Math.Pow(Vh, 0.4996667741545416);
                double a0 = 1.0 + K / Q + K * K;
                preFilter = new BiquadCoefficients
                {
                    b0 = (Vh + Vb * K / Q + K * K) / a0,
                    b1 = 2.0 * (K * K - Vh) / a0,
                    b2 = (Vh - Vb * K / Q + K * K) / a0,
                    a1 = 2.0 * (K * K - 1.0) / a0,
                    a2 = (1.0 - K / Q + K * K) / a0
                };
            }

            // RLB (revised low-frequency B-weighting) high-pass
            {
                double f0 = 38.13547087602444;
                double Q = 0.5003270373238773;
                double K = Math.Tan(Math.PI * f0 / sr);
                double a0 = 1.0 + K / Q + K * K;
                rlbFilter = new BiquadCoefficients
                {
                    b0 = 1.0 / a0,
                    b1 = -2.0 / a0,
                    b2 = 1.0 / a0,
                    a1 = 2.0 * (K * K - 1.0) / a0,
                    a2 = (1.0 - K / Q + K * K) / a0
                };
            }
        }

    }
}
