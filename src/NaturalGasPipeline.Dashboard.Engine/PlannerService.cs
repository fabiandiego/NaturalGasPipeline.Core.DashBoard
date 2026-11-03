using System.Diagnostics;
using NaturalGasPipeline.Core.Flow;
using NaturalGasPipeline.Network;
using NaturalGasPipeline.Thermodynamics;
using UnitsNet;

namespace NaturalGasPipeline.Dashboard.Engine;

public sealed record StationRow(
    string Name, double ChainageKm, double ElevationM, double SpacingKm,
    double SuctionMPa, double DischargeMPa, double Ratio,
    double IsentropicDischargeC, double HeadKJkg, double IsentropicPowerMW);

public sealed record SectionRow(
    string Name, double StartKm, double EndKm, double InletMPa, double OutletMPa,
    double FrictionDropKPa, double ElevationDropKPa, double MaxVelocityMs, double MaxErosionalRatio);

public sealed record CountRow(int Stations, bool Feasible, double? Ratio, double? PowerMW, double? DeliveryMPa, string? Reason);

public sealed record PlanAnalysis
{
    public required string Method { get; init; }
    public required string SegmentModel { get; init; }
    public required double MassFlowKgS { get; init; }
    public required double RouteLengthKm { get; init; }
    public required double DeliveryMPa { get; init; }
    public required double DeliveryTargetMPa { get; init; }
    public required double TotalPowerMW { get; init; }
    public required IReadOnlyList<StationRow> Stations { get; init; }
    public required IReadOnlyList<SectionRow> Sections { get; init; }

    /// <summary>Pressure along the route in flow order; a station shows as two points at the same chainage (suction, discharge).</summary>
    public required IReadOnlyList<(double Km, double MPa)> PressureProfile { get; init; }

    public required IReadOnlyList<(double Km, double M)> Terrain { get; init; }
    public required IReadOnlyList<CountRow> Comparison { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required long ElapsedMs { get; init; }
}

/// <summary>Question 3 of the engine: how many compressor stations, and how far apart.</summary>
public static class PlannerService
{
    public static PlanAnalysis Analyze(GasDefinition gas, PlannerInput input)
    {
        var watch = Stopwatch.StartNew();

        if (input.Route.Count < 2)
        {
            throw new ArgumentException("The route needs at least two points.");
        }

        GasMixture mixture = gas.ToMixture();
        MolarMass m = mixture.CalculateMolarMass();
        ICompressibilityModel z = CompressibilityModelFactory.Create(input.ZModel, mixture);
        var segmentModel = new IsothermalSegmentModel(m, z, new LeeGonzalezEakinViscosityModel(), input.Friction.Create());
        var t = Temperature.FromDegreesCelsius(input.FlowingTemperatureC);
        var planner = new CompressorStationPlanner(segmentModel, t, m, z, input.HeatCapacityRatio);

        StandardFlowConditions std = input.Standard.Build(z);
        StandardVolumetricFlow q = SegmentService.StandardFlow(input.FlowMMm3PerDay, std);

        ProfilePoint[] route = input.Route
            .OrderBy(r => r.ChainageKm)
            .Select(r => new ProfilePoint(Length.FromKilometers(r.ChainageKm), Length.FromMeters(r.ElevationM)))
            .ToArray();

        StationPlanningCriteria criteria = StationPlanningCriteria.Create(
            Pressure.FromMegapascals(input.InletPressureMPa),
            Pressure.FromMegapascals(input.DeliveryPressureMPa),
            input.StationDischargeMPa is double d ? (Pressure?)Pressure.FromMegapascals(d) : null,
            input.MinimumSuctionMPa is double s ? (Pressure?)Pressure.FromMegapascals(s) : null,
            Length.FromKilometers(Math.Max(0.05, input.StepKm)),
            input.MaopMPa is double maop ? (Pressure?)Pressure.FromMegapascals(maop) : null,
            input.MaximumVelocityMs is double v ? (Speed?)Speed.FromMetersPerSecond(v) : null,
            input.ErosionalConstant);

        StationPlanningProblem problem = planner.ProblemFor(
            route, Length.FromMillimeters(input.InternalDiameterMm), Length.FromMillimeters(input.RoughnessMm), q, criteria);

        StationCountComparison? comparison = null;
        var extraWarnings = new List<string>();
        try
        {
            comparison = planner.CompareStationCounts(problem, Math.Clamp(input.AdditionalStations, 0, 8));
        }
        catch (Exception ex) when (input.Placement != PlacementChoice.Automatic)
        {
            extraWarnings.Add($"Station-count comparison not available: {ex.Message}");
        }

        StationPlan plan = input.Placement switch
        {
            PlacementChoice.MaximumSpacing => comparison?.MaximumSpacing ?? planner.Plan(problem, new MaximumSpacingPlacement()),
            PlacementChoice.EqualRatio => planner.Plan(problem, new EqualRatioPlacement(Math.Max(0, input.StationCount))),
            _ => Automatic(planner, problem, comparison!)
        };

        var profile = new List<(double, double)>();
        foreach (PipelineSection section in plan.Sections)
        {
            double x = section.StartChainage.Kilometers;
            profile.Add((x, section.InletPressure.Megapascals));
            foreach (SegmentResult seg in section.Segments)
            {
                x += seg.Segment.Length.Kilometers;
                profile.Add((x, seg.OutletPressure.Megapascals));
            }
        }

        var comparisonRows = comparison is null
            ? new List<CountRow>()
            : comparison.Options.Select(o => new CountRow(
                o.StationCount,
                o.Feasible,
                double.IsFinite(o.PressureRatio) ? (double?)o.PressureRatio : null,
                o.Plan is null ? (double?)null : (double)o.TotalIsentropicPower.Megawatts,
                o.Plan?.DeliveryPressure.Megapascals,
                o.Reason)).ToList();

        return new PlanAnalysis
        {
            Method = plan.Method,
            SegmentModel = segmentModel.Name + " · Z " + EngineCatalog.ModelName(z, input.ZModel),
            MassFlowKgS = plan.MassFlow.KilogramsPerSecond,
            RouteLengthKm = plan.RouteLength.Kilometers,
            DeliveryMPa = plan.DeliveryPressure.Megapascals,
            DeliveryTargetMPa = plan.DeliveryPressureTarget.Megapascals,
            TotalPowerMW = (double)plan.TotalIsentropicPower.Megawatts,
            Stations = plan.Stations.Select(st => new StationRow(
                st.Name, st.Chainage.Kilometers, st.Elevation.Meters, st.DistanceFromPrevious.Kilometers,
                st.SuctionPressure.Megapascals, st.DischargePressure.Megapascals, st.PressureRatio,
                st.IsentropicDischargeTemperature.DegreesCelsius, st.IsentropicHead.KilojoulesPerKilogram,
                (double)st.IsentropicPower.Megawatts)).ToList(),
            Sections = plan.Sections.Select(sec => new SectionRow(
                sec.Name, sec.StartChainage.Kilometers, sec.EndChainage.Kilometers,
                sec.InletPressure.Megapascals, sec.OutletPressure.Megapascals,
                sec.FrictionPressureDrop.Kilopascals, sec.ElevationPressureDrop.Kilopascals,
                sec.MaximumVelocity.MetersPerSecond, sec.MaximumErosionalRatio)).ToList(),
            PressureProfile = profile,
            Terrain = route.Select(r => (r.Chainage.Kilometers, r.Elevation.Meters)).ToList(),
            Comparison = comparisonRows,
            Warnings = plan.Warnings.Concat(extraWarnings).ToList(),
            ElapsedMs = watch.ElapsedMilliseconds
        };
    }

    /// <summary>
    /// The choice the Unity front end makes (EngineBridge.Run, mirrored by the
    /// engine test RealisticCasesTests): no station if none is needed;
    /// otherwise equal ratios at the minimum feasible count.
    /// </summary>
    private static StationPlan Automatic(CompressorStationPlanner planner, StationPlanningProblem problem, StationCountComparison comparison)
    {
        if (comparison.MinimumStationCount == 0)
        {
            return comparison.MaximumSpacing;
        }

        StationCountOption? option = comparison.Options.FirstOrDefault(o => o.StationCount == comparison.MinimumStationCount && o.Feasible);
        return option?.Plan ?? planner.Plan(problem, new EqualRatioPlacement(comparison.MinimumStationCount));
    }
}
