using NaturalGasPipeline.Core.Common;
using NaturalGasPipeline.Core.Flow;
using NaturalGasPipeline.Core.Pipeline;
using NaturalGasPipeline.Correlations;
using NaturalGasPipeline.Hydraulics;
using NaturalGasPipeline.Network;
using NaturalGasPipeline.Thermodynamics;
using UnitsNet;

namespace NaturalGasPipeline.Dashboard.Engine;

public sealed record EquationRow(string Equation, double? OutletPressureMPa, double? TransmissionFactor, double? ReynoldsNumber, double? DifferencePercent, IReadOnlyList<string> Warnings, string? Error);

public sealed record SegmentAnalysis
{
    public required SegmentSolveMode Mode { get; init; }
    public required string ZModel { get; init; }
    public required string FrictionModel { get; init; }
    public required double InletPressureMPa { get; init; }
    public required double OutletPressureMPa { get; init; }
    public required double FlowMMm3PerDay { get; init; }
    public required double MassFlowKgS { get; init; }
    public required double InternalDiameterMm { get; init; }
    public required double LengthKm { get; init; }
    public required double ElevationChangeM { get; init; }
    public required double AveragePressureMPa { get; init; }
    public required double AverageZ { get; init; }
    public required double AverageDensityKgM3 { get; init; }
    public required double AverageViscosityMicroPaS { get; init; }
    public required double ReynoldsNumber { get; init; }
    public required double RelativeRoughness { get; init; }
    public required double DarcyFrictionFactor { get; init; }
    public required double TransmissionFactor { get; init; }
    public required double ElevationParameter { get; init; }
    public required double InletVelocityMs { get; init; }
    public required double OutletVelocityMs { get; init; }
    public required double ErosionalVelocityMs { get; init; }
    public required double FrictionDropKPa { get; init; }
    public required double ElevationDropKPa { get; init; }
    public required int Iterations { get; init; }
    public required double ZStandard { get; init; }
    public required IReadOnlyList<(double Km, double MPa)> Profile { get; init; }
    public required IReadOnlyList<EquationRow> Equations { get; init; }
    public required IReadOnlyList<Series> MoodyCurves { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

/// <summary>Question 2 of the engine: pressure loss from A to B (friction, elevation, distance).</summary>
public static class SegmentService
{
    public static SegmentAnalysis Analyze(GasDefinition gas, SegmentInput input)
    {
        GasMixture mixture = gas.ToMixture();
        MolarMass m = mixture.CalculateMolarMass();
        double sg = NaturalGasPipeline.Thermodynamics.SpecificGravity.OfMixture(mixture, GasService.AirMolarMass);
        ICompressibilityModel z = CompressibilityModelFactory.Create(input.ZModel, mixture);
        var viscosity = new LeeGonzalezEakinViscosityModel();
        IFrictionFactorModel friction = input.Friction.Create();
        var warnings = new List<string>();

        var t = Temperature.FromDegreesCelsius(input.FlowingTemperatureC);
        var p1 = Pressure.FromMegapascals(input.InletPressureMPa);
        var roughness = Length.FromMillimeters(input.RoughnessMm);
        var length = Length.FromKilometers(input.LengthKm);
        var dz = Length.FromMeters(input.ElevationChangeM);
        StandardFlowConditions std = input.Standard.Build(z);
        var solver = new IsothermalGasFlowSolver(m, z, viscosity, friction);
        var gasForEquations = new FlowEquationGas(sg, GasService.AirMolarMass, z, viscosity);

        // Resolve the unknown of the chosen mode, then solve the segment for P2.
        double diameterMm = input.InternalDiameterMm;
        MassFlow massFlow;
        StandardVolumetricFlow q;

        switch (input.Mode)
        {
            case SegmentSolveMode.FlowRate:
            {
                var segment = new PipeSegment(Length.FromMillimeters(diameterMm), length, roughness, dz);
                GasFlowSolution flow = solver.SolveMassFlow(segment, t, p1, Pressure.FromMegapascals(input.OutletPressureMPa));
                massFlow = flow.MassFlow;
                q = FlowRateConverter.ConvertMassToStandardVolume(massFlow, m, std);
                break;
            }
            case SegmentSolveMode.InternalDiameter:
            {
                q = StandardFlow(input.FlowMMm3PerDay, std);
                var sizing = new GasFlowEquationSolver(new GeneralFlowEquation(friction), gasForEquations, std);
                FlowEquationSolution d = sizing.SolveInternalDiameter(
                    length, roughness, dz, t, p1, Pressure.FromMegapascals(input.OutletPressureMPa), q);
                diameterMm = d.InternalDiameter.Millimeters;
                massFlow = FlowRateConverter.ConvertStandardVolumeToMass(q, m);
                warnings.AddRange(d.Warnings.Select(w => $"Sizing (General Flow): {w}"));
                break;
            }
            default:
                q = StandardFlow(input.FlowMMm3PerDay, std);
                massFlow = FlowRateConverter.ConvertStandardVolumeToMass(q, m);
                break;
        }

        var pipe = new PipeSegment(Length.FromMillimeters(diameterMm), length, roughness, dz);
        GasFlowSolution s = solver.SolveOutletPressure(pipe, t, p1, massFlow);
        warnings.AddRange(s.Warnings);

        // Friction / elevation split, as the network module reports it.
        var segmentModel = new IsothermalSegmentModel(m, z, viscosity, friction);
        SegmentResult split = segmentModel.SolveOutletPressure(pipe, t, p1, massFlow);

        // Erosional velocity at the inlet (lowest velocity limit is at the highest density there).
        double z1 = z.Calculate(p1, t);
        var rho1 = Density.FromKilogramsPerCubicMeter(p1.Pascals * m.KilogramsPerMole / (z1 * PhysicalConstants.MolarGasConstant * t.Kelvins));
        Speed ve = GasVelocityCalculator.ErosionalVelocity(rho1);
        double vMax = Math.Max(s.InletVelocity.MetersPerSecond, s.OutletVelocity.MetersPerSecond);
        if (vMax > ve.MetersPerSecond)
        {
            warnings.Add($"Velocity {vMax:F1} m/s exceeds the API RP 14E erosional velocity {ve.MetersPerSecond:F1} m/s (C = 100).");
        }

        // Pressure profile along the pipe: the same solver over equal sub-segments.
        var profile = new List<(double, double)> { (0.0, p1.Megapascals) };
        int parts = Math.Clamp(input.ProfileParts, 2, 400);
        double x = 0.0;
        Pressure p = p1;
        foreach (PipeSegment part in PipelineProfile.Subdivide(pipe, parts))
        {
            try
            {
                p = solver.SolveOutletPressure(part, t, p, massFlow).OutletPressure;
            }
            catch (Exception)
            {
                break;
            }

            x += part.Length.Kilometers;
            profile.Add((x, p.Megapascals));
        }

        // Same pipe and flow with every flow equation of the Correlations module.
        var equations = new List<EquationRow>();
        foreach (IGasFlowEquation eq in FlowEquations(friction, input.PipelineEfficiency))
        {
            try
            {
                var eqSolver = new GasFlowEquationSolver(eq, gasForEquations, std);
                FlowEquationSolution r = eqSolver.SolveOutletPressure(pipe, t, p1, q);
                double diff = (r.OutletPressure.Pascals - s.OutletPressure.Pascals) / s.OutletPressure.Pascals * 100.0;
                equations.Add(new EquationRow(r.Equation, r.OutletPressure.Megapascals, r.TransmissionFactor, r.ReynoldsNumber, diff, r.Warnings, null));
            }
            catch (Exception ex)
            {
                equations.Add(new EquationRow(eq.Name, null, null, null, null, Array.Empty<string>(), ex.Message));
            }
        }

        double rr = FrictionFactorModelExtensions.RelativeRoughness(pipe.InternalDiameter, roughness);

        return new SegmentAnalysis
        {
            Mode = input.Mode,
            ZModel = EngineCatalog.ModelName(z, input.ZModel),
            FrictionModel = friction.Name,
            InletPressureMPa = s.InletPressure.Megapascals,
            OutletPressureMPa = s.OutletPressure.Megapascals,
            FlowMMm3PerDay = q.Value.CubicMetersPerSecond * 86400.0 / 1.0e6,
            MassFlowKgS = massFlow.KilogramsPerSecond,
            InternalDiameterMm = diameterMm,
            LengthKm = input.LengthKm,
            ElevationChangeM = input.ElevationChangeM,
            AveragePressureMPa = s.AveragePressure.Megapascals,
            AverageZ = s.AverageCompressibilityFactor,
            AverageDensityKgM3 = s.AverageDensity.KilogramsPerCubicMeter,
            AverageViscosityMicroPaS = s.AverageViscosity.MicropascalSeconds,
            ReynoldsNumber = s.ReynoldsNumber,
            RelativeRoughness = rr,
            DarcyFrictionFactor = s.DarcyFrictionFactor,
            TransmissionFactor = FrictionFactorModelExtensions.TransmissionFactorFromDarcy(s.DarcyFrictionFactor),
            ElevationParameter = s.ElevationParameter,
            InletVelocityMs = s.InletVelocity.MetersPerSecond,
            OutletVelocityMs = s.OutletVelocity.MetersPerSecond,
            ErosionalVelocityMs = ve.MetersPerSecond,
            FrictionDropKPa = split.FrictionPressureDrop.Kilopascals,
            ElevationDropKPa = split.ElevationPressureDrop.Kilopascals,
            Iterations = s.Iterations,
            ZStandard = std.CompressibilityFactor,
            Profile = profile,
            Equations = equations,
            MoodyCurves = Moody(friction, rr),
            Warnings = warnings
        };
    }

    public static StandardVolumetricFlow StandardFlow(double mmm3PerDay, StandardFlowConditions std) =>
        new(VolumeFlow.FromCubicMetersPerSecond(mmm3PerDay * 1.0e6 / 86400.0), std);

    /// <summary>The flow equations of the Correlations module, with the user's friction model for the General Flow equation.</summary>
    public static IEnumerable<IGasFlowEquation> FlowEquations(IFrictionFactorModel friction, double efficiency)
    {
        yield return new GeneralFlowEquation(friction);
        yield return new GeneralFlowEquation(new AgaTransmissionFactorModel(0.95));
        yield return GasFlowEquations.Weymouth(efficiency);
        yield return GasFlowEquations.PanhandleA(efficiency);
        yield return GasFlowEquations.PanhandleB(efficiency);
    }

    /// <summary>
    /// Moody chart drawn with the selected friction model: f(Re) for a set of
    /// relative roughnesses, plus the laminar line 64/Re.
    /// </summary>
    public static IReadOnlyList<Series> Moody(IFrictionFactorModel friction, double operatingRelativeRoughness)
    {
        var series = new List<Series>();
        var laminar = new List<(double, double)>();
        for (double re = 600; re <= 2300; re *= 1.15)
        {
            laminar.Add((re, 64.0 / re));
        }

        series.Add(new Series("Laminar 64/Re", laminar));

        double[] roughness = { 0.0, 1e-6, 1e-5, 5e-5, 1e-4, 5e-4, 1e-3, 5e-3 };
        foreach (double rr in roughness.Append(operatingRelativeRoughness).Distinct())
        {
            var pts = new List<(double, double)>();
            for (double logRe = 3.6; logRe <= 8.0001; logRe += 0.05)
            {
                double re = Math.Pow(10, logRe);
                try
                {
                    double f = friction.CalculateDarcyFrictionFactor(re, rr);
                    if (double.IsFinite(f) && f > 0)
                    {
                        pts.Add((re, f));
                    }
                }
                catch
                {
                    // Outside what the model accepts.
                }
            }

            string label = rr == operatingRelativeRoughness ? $"ε/D = {rr:0.###E+0} (this pipe)" : rr == 0 ? "smooth" : $"ε/D = {rr:0.#E+0}";
            series.Add(new Series(label, pts));
        }

        return series;
    }
}
