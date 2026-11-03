using System.Globalization;

namespace NaturalGasPipeline.Dashboard.Components;

public sealed record ChartSeries(
    string Name,
    IReadOnlyList<(double X, double Y)> Points,
    string Color,
    bool Dashed = false,
    double Width = 2.0,
    bool Area = false,
    bool Markers = false,
    bool InLegend = true);

/// <summary>Vertical reference line (e.g. a compressor station).</summary>
public sealed record ChartVLine(double X, string Label, string Color, bool Dashed = true);

/// <summary>Horizontal reference line (e.g. MAOP, delivery pressure).</summary>
public sealed record ChartHLine(double Y, string Label, string Color, bool Dashed = true);

/// <summary>Highlighted point (e.g. the operating point on the Moody chart).</summary>
public sealed record ChartPoint(double X, double Y, string Label, string Color);

public static class Palette
{
    public const string Blue = "#0a5ba8";
    public const string Sky = "#3f8fd8";
    public const string Navy = "#063a6e";
    public const string Teal = "#00908c";
    public const string Flame = "#e8a317";
    public const string Red = "#c53030";
    public const string Steel = "#6b7a8f";
    public const string Violet = "#7b55c7";
    public const string Green = "#2f8f4e";
    public const string Earth = "#9a7b4f";

    /// <summary>Ordered so that neighbours stay distinguishable.</summary>
    public static readonly string[] Series =
    {
        Blue, Flame, Teal, Red, Violet, Green, Sky, Earth, Navy, "#d16ba5", Steel, "#5aa9a5", "#b5651d"
    };

    public static string At(int i) => Series[((i % Series.Length) + Series.Length) % Series.Length];
}

public static class Fmt
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string N(double v, int decimals = 2) =>
        double.IsFinite(v) ? v.ToString("N" + decimals, Inv) : "—";

    public static string N(double? v, int decimals = 2) => v is double d ? N(d, decimals) : "—";

    public static string E(double v, int decimals = 3) =>
        double.IsFinite(v) ? v.ToString("0." + new string('0', decimals) + "E+0", Inv) : "—";

    /// <summary>SVG coordinate (invariant, short).</summary>
    public static string S(double v) => double.IsFinite(v) ? Math.Round(v, 2).ToString(Inv) : "0";

    public static string Pct(double v, int decimals = 1) => N(v * 100.0, decimals) + " %";
}
