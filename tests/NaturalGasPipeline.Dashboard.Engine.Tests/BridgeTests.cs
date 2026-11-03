using NaturalGasPipeline.Dashboard.Engine;
using NaturalGasPipeline.Thermodynamics;
using Xunit.Abstractions;

namespace NaturalGasPipeline.Dashboard.Engine.Tests;

/// <summary>
/// The dashboard bridge must give the same answers as the engine's own
/// regression cases. These run against whatever engine version the
/// submodule points to, so an engine update that changes a published
/// result fails the dashboard build before it is deployed.
/// </summary>
public class BridgeTests
{
    private readonly ITestOutputHelper _output;

    public BridgeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Catalog_DiscoversEngineComponentsAndModels()
    {
        Assert.Contains("Methane", EngineCatalog.ComponentNames);
        Assert.Contains("CarbonDioxide", EngineCatalog.ComponentNames);
        Assert.Contains(CompressibilityModelType.Aga8Detail, EngineCatalog.CompressibilityModels);
        Assert.NotEmpty(EngineCatalog.FrictionModels);
        Assert.NotEmpty(EngineCatalog.PipeGrades);
        Assert.Equal("Soave Redlich Kwong", EngineCatalog.Humanize("SoaveRedlichKwong"));
        Assert.Equal("Class 1 Division 2", EngineCatalog.Humanize("Class1Division2"));

        foreach ((string module, string version) in EngineCatalog.Modules())
        {
            _output.WriteLine($"{module} {version}");
        }

        foreach (var item in EngineCatalog.PendingReviewItems())
        {
            _output.WriteLine($"[{item.Item}] {item.Member}: {item.Reason}");
        }
    }

    // Station counts published for R1–R3 and engine regression values for S1–S2
    // (engine/tests/NaturalGasPipeline.Network.Tests/RealisticCasesTests.cs).
    [Theory]
    [InlineData("R1", 0)]
    [InlineData("R2", 0)]
    [InlineData("R3", 3)]
    [InlineData("S1", 8)]
    [InlineData("S2", 1)]
    public void Planner_ReproducesEngineExampleCases(string id, int expectedStations)
    {
        Presets.ExampleCase c = Presets.Cases.Single(x => x.Id == id);
        var gas = new GasDefinition();
        var input = new PlannerInput();
        Presets.Apply(c, input, gas);

        PlanAnalysis plan = PlannerService.Analyze(gas, input);

        _output.WriteLine($"{id}: {plan.Method}, N = {plan.Stations.Count}, P = {plan.TotalPowerMW:F2} MW, delivery {plan.DeliveryMPa:F3} MPa, {plan.ElapsedMs} ms");
        Assert.Equal(expectedStations, plan.Stations.Count);
        Assert.True(plan.DeliveryMPa >= c.DeliveryMPa - 1e-6);

        // Profile is continuous in chainage and ends at the route end.
        Assert.Equal(plan.RouteLengthKm, plan.PressureProfile[^1].Km, 6);
        for (int i = 1; i < plan.PressureProfile.Count; i++)
        {
            Assert.True(plan.PressureProfile[i].Km >= plan.PressureProfile[i - 1].Km - 1e-9);
        }
    }

    [Fact]
    public void Gas_AllModelsEvaluateForTypicalGas()
    {
        var gas = new GasDefinition();
        gas.Set(Presets.Gases()[0].Composition);
        GasAnalysis a = GasService.Analyze(gas, 7.0, 15.0, CompressibilityModelType.Aga8Detail, new StandardConditionsInput());

        foreach (ZRow row in a.ZTable)
        {
            _output.WriteLine($"{row.Model,-28} {row.Basis,-28} Z = {row.Z:F5}  dev {row.DeviationFromAga8Percent:F2} %  {row.Error}");
        }

        Assert.InRange(a.Z, 0.80, 0.95);
        Assert.InRange(a.MolarMassGPerMol, 16.0, 20.0);
        Assert.True(a.ViscosityMicroPaS > 5.0);
        Assert.All(a.ZTable.Where(r => r.Error is null), r => Assert.InRange(r.Z!.Value, 0.5, 1.2));
        Assert.NotEmpty(a.ZCurves);
    }

    [Fact]
    public void Gas_Aga8CompositionRangeIsReportedOncePerGas()
    {
        var gas = new GasDefinition();
        gas.Set(Presets.Cases.Single(c => c.Id == "R3").Composition);
        GasAnalysis inside = GasService.Analyze(gas, 7.0, 15.0, CompressibilityModelType.Aga8Detail, new StandardConditionsInput());
        Assert.True(inside.Aga8CompositionInNormalRange);

        // 1 mol % H2S exceeds the AGA8 normal-range limit of 0.02 mol %.
        gas.Set(new[] { ("Methane", 95.0), ("Ethane", 3.0), ("Nitrogen", 1.0), ("HydrogenSulfide", 1.0) });
        GasAnalysis sour = GasService.Analyze(gas, 7.0, 15.0, CompressibilityModelType.Aga8Detail, new StandardConditionsInput());
        _output.WriteLine("AGA8 composition: " + string.Join("; ", sour.Aga8CompositionViolations));
        Assert.False(sour.Aga8CompositionInNormalRange);
        Assert.NotEmpty(sour.Aga8CompositionViolations);
        Assert.Single(sour.Warnings, w => w.Contains("AGA8-DC92 normal range"));
    }

    [Fact]
    public void Segment_SolveModesAreConsistent()
    {
        var gas = new GasDefinition();
        gas.Set(Presets.Gases()[0].Composition);
        var input = new SegmentInput();

        SegmentAnalysis p2 = SegmentService.Analyze(gas, input);
        _output.WriteLine($"P2 = {p2.OutletPressureMPa:F4} MPa, Re = {p2.ReynoldsNumber:E3}, f = {p2.DarcyFrictionFactor:F5}, v1 = {p2.InletVelocityMs:F2} m/s");
        foreach (EquationRow e in p2.Equations)
        {
            _output.WriteLine($"  {e.Equation,-34} P2 = {e.OutletPressureMPa:F4} ({e.DifferencePercent:F2} %) {e.Error}");
        }

        Assert.True(p2.OutletPressureMPa < input.InletPressureMPa);
        Assert.Equal(p2.OutletPressureMPa, p2.Profile[^1].MPa, 2);

        input.Mode = SegmentSolveMode.FlowRate;
        input.OutletPressureMPa = p2.OutletPressureMPa;
        SegmentAnalysis q = SegmentService.Analyze(gas, input);
        Assert.Equal(input.FlowMMm3PerDay, q.FlowMMm3PerDay, 4);

        input.Mode = SegmentSolveMode.InternalDiameter;
        SegmentAnalysis d = SegmentService.Analyze(gas, input);
        _output.WriteLine($"D (General Flow) = {d.InternalDiameterMm:F1} mm for {input.InternalDiameterMm} mm");
        Assert.InRange(d.InternalDiameterMm, 0.97 * input.InternalDiameterMm, 1.03 * input.InternalDiameterMm);
    }

    [Fact]
    public void Compressor_StagesMultiplyToTheOverallRatio()
    {
        var gas = new GasDefinition();
        gas.Set(Presets.Gases()[0].Composition);
        var input = new CompressorInput();
        CompressorAnalysis r = CompressorService.Analyze(gas, input);

        double product = r.Stages.Aggregate(1.0, (acc, s) => acc * s.Ratio);
        _output.WriteLine($"r = {r.OverallRatio:F3}, gas {r.GasPowerMW:F2} MW, shaft {r.ShaftPowerMW:F2} MW, T2 {r.DischargeC:F1} °C");
        Assert.Equal(r.OverallRatio, product, 6);
        Assert.True(r.ShaftPowerMW > r.GasPowerMW);
    }

    [Fact]
    public void Mechanics_DesignPressureFollowsBarlow()
    {
        var input = new MechanicsInput();
        MechanicsAnalysis r = MechanicsService.Analyze(input);

        double barlow = 2.0 * r.SmysMPa * input.WallThicknessMm / input.OutsideDiameterMm * r.DesignFactor * r.JointFactor * r.TemperatureFactor;
        Assert.Equal(barlow, r.DesignPressureGaugeMPa, 6);
        Assert.True(r.RequiredWallMm < input.WallThicknessMm == r.WithinDesignPressure);
    }
}
