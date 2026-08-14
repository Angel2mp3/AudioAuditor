using System;

namespace AudioQualityChecker.Services;

/// <summary>
/// A gradient brush's two endpoints in relative (0..1) brush space. Values are deliberately
/// not clamped to that range: the sweep modes push an endpoint outside the brush so the
/// visible band slides past continuously instead of snapping at the edge.
/// </summary>
public readonly record struct GlowGradientLine(
    double StartX, double StartY, double EndX, double EndY);

/// <summary>
/// The pure geometry behind the Now Playing cover-glow motion modes: each takes the running
/// animation phase in radians and returns where the gradient's endpoints sit that frame.
///
/// Split out from the view so the phase arithmetic — in particular the wrap that keeps the
/// sweep modes continuous across the seam, and across negative phases — is testable. Mirrors
/// the NpMoveGlowBrush* helpers in the WPF build's Windows/NpColors.GlowPulse.cs.
/// </summary>
public static class NpGlowMotion
{
    private const double TwoPi = Math.PI * 2.0;

    /// <summary>Rotates the gradient about the brush centre, endpoints on the unit circle's half-radius.</summary>
    public static GlowGradientLine Swirl(double phase)
    {
        double cos = Math.Cos(phase);
        double sin = Math.Sin(phase);
        return new GlowGradientLine(
            0.5 - cos * 0.5, 0.5 - sin * 0.5,
            0.5 + cos * 0.5, 0.5 + sin * 0.5);
    }

    /// <summary>
    /// Sweeps the gradient horizontally, y centred for a clean band. The visible band spans 0.4
    /// of the brush width and slides through the full [0,1) range, so it leaves one edge as it
    /// enters the other rather than jumping back.
    /// </summary>
    public static GlowGradientLine Linear(double phase, bool leftToRight)
    {
        double t = Wrap01(phase);
        double start = leftToRight ? t - 0.2 : 1.0 - t - 0.2;
        return new GlowGradientLine(start, 0.5, start + 0.4, 0.5);
    }

    /// <summary>Sweeps corner to corner, the band spanning 0.5 of the diagonal.</summary>
    public static GlowGradientLine Diagonal(double phase)
    {
        double start = Wrap01(phase) - 0.25;
        double end = start + 0.5;
        return new GlowGradientLine(start, start, end, end);
    }

    /// <summary>
    /// Carries both endpoints around their own small circles, a quarter turn apart, so the
    /// gradient's angle and its offset both drift instead of pivoting about a fixed centre.
    /// </summary>
    public static GlowGradientLine Orbit(double phase)
    {
        double cos = Math.Cos(phase);
        double sin = Math.Sin(phase);
        return new GlowGradientLine(
            0.35 + cos * 0.28, 0.35 + sin * 0.28,
            0.65 - sin * 0.28, 0.65 + cos * 0.28);
    }

    /// <summary>
    /// Maps a phase in radians onto [0,1). C#'s % keeps the sign of the dividend, so a negative
    /// phase — which happens as soon as a caller offsets one brush behind another — would come
    /// back negative and put the band a full width off. The correction is what makes the sweep
    /// continuous in both directions.
    /// </summary>
    private static double Wrap01(double phase)
    {
        double t = (phase / TwoPi) % 1.0;
        return t < 0 ? t + 1.0 : t;
    }
}
