using System.Reflection;
using NaturalGasPipeline.Compression;
using NaturalGasPipeline.Hydraulics;
using NaturalGasPipeline.Mechanics;
using NaturalGasPipeline.Thermodynamics;

namespace NaturalGasPipeline.Dashboard.Engine;

/// <summary>
/// What the engine can do, discovered from the engine itself (enums,
/// factories, static catalogues) rather than hard-coded here. When the
/// engine submodule is updated and gains a component, a Z model, a friction
/// correlation, a pipe grade or a location class, it appears in the
/// dashboard without changing this code.
/// </summary>
public static class EngineCatalog
{
    private static readonly Lazy<IReadOnlyList<PropertyInfo>> ComponentProperties = new(() =>
        typeof(StandardGasComponents)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(GasComponent))
            .ToArray());

    /// <summary>Names of the components in <see cref="StandardGasComponents"/>, as the engine names them.</summary>
    public static IReadOnlyList<string> ComponentNames { get; } =
        ComponentProperties.Value.Select(p => ((GasComponent)p.GetValue(null)!).Name).ToArray();

    /// <summary>A fresh component instance (the engine returns a new object on every access).</summary>
    public static GasComponent CreateComponent(string name)
    {
        foreach (PropertyInfo p in ComponentProperties.Value)
        {
            var c = (GasComponent)p.GetValue(null)!;

            if (c.Name == name)
            {
                return c;
            }
        }

        throw new ArgumentException($"The engine has no standard component '{name}'.", nameof(name));
    }

    public static IReadOnlyList<CompressibilityModelType> CompressibilityModels { get; } =
        Enum.GetValues(typeof(CompressibilityModelType)).Cast<CompressibilityModelType>().ToArray();

    public static IReadOnlyList<FrictionFactorModelType> FrictionModels { get; } =
        Enum.GetValues(typeof(FrictionFactorModelType)).Cast<FrictionFactorModelType>().ToArray();

    public static IReadOnlyList<LinePipeGrade> PipeGrades => ApiLinePipeGrades.All;

    public static IReadOnlyList<LocationClass> LocationClasses { get; } =
        Enum.GetValues(typeof(LocationClass)).Cast<LocationClass>().ToArray();

    public static IReadOnlyList<LongitudinalJoint> Joints { get; } =
        Enum.GetValues(typeof(LongitudinalJoint)).Cast<LongitudinalJoint>().ToArray();

    public static IReadOnlyList<CompressionEfficiencyBasis> EfficiencyBases { get; } =
        Enum.GetValues(typeof(CompressionEfficiencyBasis)).Cast<CompressionEfficiencyBasis>().ToArray();

    /// <summary>Readable label for an enum value: "SoaveRedlichKwong" → "Soave Redlich Kwong".</summary>
    public static string Label<T>(T value) where T : struct, Enum => Humanize(value.ToString());

    public static string Humanize(string pascal)
    {
        var sb = new System.Text.StringBuilder(pascal.Length + 8);

        for (int i = 0; i < pascal.Length; i++)
        {
            char ch = pascal[i];
            bool boundary = i > 0 && char.IsUpper(ch) && (char.IsLower(pascal[i - 1]) || (i + 1 < pascal.Length && char.IsLower(pascal[i + 1])));
            bool digit = i > 0 && char.IsDigit(ch) && !char.IsDigit(pascal[i - 1]);

            if (boundary || digit)
            {
                sb.Append(' ');
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }

    /// <summary>Name the engine itself gives the model, if it has one.</summary>
    public static string ModelName(ICompressibilityModel model, CompressibilityModelType type) =>
        model is ICompressibilityCorrelation c ? c.Name : Humanize(type.ToString());

    /// <summary>
    /// Members of the engine marked <c>[PendingReview]</c> (mechanics items
    /// not yet signed off by the owner), read by reflection so the list
    /// follows the engine.
    /// </summary>
    public static IReadOnlyList<(string Member, string Item, string Reason)> PendingReviewItems()
    {
        var list = new List<(string, string, string)>();
        Assembly asm = typeof(PendingReviewAttribute).Assembly;
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        foreach (Type t in asm.GetTypes())
        {
            foreach (PendingReviewAttribute a in t.GetCustomAttributes<PendingReviewAttribute>(false))
            {
                list.Add((t.Name, a.ChecklistItem, a.Reason));
            }

            foreach (MemberInfo m in t.GetMembers(all))
            {
                foreach (PendingReviewAttribute a in m.GetCustomAttributes<PendingReviewAttribute>(false))
                {
                    list.Add(($"{t.Name}.{m.Name}", a.ChecklistItem, a.Reason));
                }
            }
        }

        return list.Distinct().OrderBy(x => x.Item2).ToArray();
    }

    /// <summary>Assembly versions of the engine modules loaded by the dashboard.</summary>
    public static IReadOnlyList<(string Module, string Version)> Modules()
    {
        Type[] anchors =
        {
            typeof(NaturalGasPipeline.Core.Pipeline.PipeSegment),
            typeof(GasMixture),
            typeof(IsothermalGasFlowSolver),
            typeof(NaturalGasPipeline.Correlations.GasFlowEquationSolver),
            typeof(CompressorStage),
            typeof(NaturalGasPipeline.Network.CompressorStationPlanner),
            typeof(SteelPipeDesign)
        };

        return anchors.Select(t =>
        {
            Assembly a = t.Assembly;
            string v = a.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                       ?? a.GetName().Version?.ToString() ?? "?";
            return (a.GetName().Name ?? t.Namespace ?? "?", v);
        }).ToArray();
    }
}
