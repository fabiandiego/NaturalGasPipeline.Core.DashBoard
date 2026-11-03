using NaturalGasPipeline.Compression;
using NaturalGasPipeline.Core.Flow;
using NaturalGasPipeline.Mechanics;
using NaturalGasPipeline.Thermodynamics;
using UnitsNet;

namespace NaturalGasPipeline.Dashboard.Engine;

public sealed record StageRow(
    int Stage, double SuctionMPa, double SuctionC, double DischargeMPa, double DischargeC,
    double Ratio, double SuctionZ, double DischargeZ, double HeadKJkg, double GasPowerMW, double ShaftPowerMW);

public sealed record CompressorAnalysis
{
    public required string ZModel { get; init; }
    public required double MassFlowKgS { get; init; }
    public required double OverallRatio { get; init; }
    public required double DischargeC { get; init; }
    public required double GasPowerMW { get; init; }
    public required double ShaftPowerMW { get; init; }
    public required double? IntercoolerC { get; init; }
    public required IReadOnlyList<StageRow> Stages { get; init; }
}

/// <summary>Question 4 of the engine: what each station is like and how much power it needs.</summary>
public static class CompressorService
{
    public static CompressorAnalysis Analyze(GasDefinition gas, CompressorInput input)
    {
        GasMixture mixture = gas.ToMixture();
        MolarMass m = mixture.CalculateMolarMass();
        ICompressibilityModel z = CompressibilityModelFactory.Create(input.ZModel, mixture);
        StandardFlowConditions std = input.Standard.Build(z);
        MassFlow flow = FlowRateConverter.ConvertStandardVolumeToMass(SegmentService.StandardFlow(input.FlowMMm3PerDay, std), m);

        var stage = new CompressorStage(m, z, input.HeatCapacityRatio, input.Basis, input.Efficiency, input.MechanicalEfficiency);
        Temperature? cooler = input.IntercoolerOutletC is double c ? Temperature.FromDegreesCelsius(c) : (Temperature?)null;
        var station = new CompressorStation("CS", stage, Math.Max(1, input.Stages), cooler);

        CompressorStationResult r = station.Compress(
            Pressure.FromMegapascals(input.SuctionPressureMPa),
            Temperature.FromDegreesCelsius(input.SuctionTemperatureC),
            Pressure.FromMegapascals(input.DischargePressureMPa),
            flow);

        return new CompressorAnalysis
        {
            ZModel = EngineCatalog.ModelName(z, input.ZModel),
            MassFlowKgS = flow.KilogramsPerSecond,
            OverallRatio = r.PressureRatio,
            DischargeC = r.DischargeTemperature.DegreesCelsius,
            GasPowerMW = (double)r.GasPower.Megawatts,
            ShaftPowerMW = (double)r.ShaftPower.Megawatts,
            IntercoolerC = input.IntercoolerOutletC,
            Stages = r.Stages.Select((s, i) => new StageRow(
                i + 1,
                s.SuctionPressure.Megapascals, s.SuctionTemperature.DegreesCelsius,
                s.DischargePressure.Megapascals, s.DischargeTemperature.DegreesCelsius,
                s.PressureRatio, s.SuctionCompressibilityFactor, s.DischargeCompressibilityFactor,
                s.Head.KilojoulesPerKilogram, (double)s.GasPower.Megawatts, (double)s.ShaftPower.Megawatts)).ToList()
        };
    }
}

public sealed record MechanicsAnalysis
{
    public required string Grade { get; init; }
    public required double SmysMPa { get; init; }
    public required double DesignFactor { get; init; }
    public required double JointFactor { get; init; }
    public required double TemperatureFactor { get; init; }
    public required double AllowableHoopMPa { get; init; }
    public required double DesignPressureGaugeMPa { get; init; }
    public required double DesignPressureAbsoluteMPa { get; init; }
    public required double OperatingGaugeMPa { get; init; }
    public required double HoopStressMPa { get; init; }
    public required double FractionOfSmys { get; init; }
    public required double DiameterToThickness { get; init; }
    public required bool WithinDesignPressure { get; init; }
    public required double RequiredWallMm { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

/// <summary>Wall thickness and design pressure (ASME B31.8 / 49 CFR 192 form). The engine marks this module as pending review.</summary>
public static class MechanicsService
{
    public static MechanicsAnalysis Analyze(MechanicsInput input)
    {
        LinePipeGrade grade = EngineCatalog.PipeGrades.FirstOrDefault(g => g.Name == input.GradeName)
                              ?? throw new ArgumentException($"Unknown grade '{input.GradeName}'.");
        PipeDesignBasis basis = PipeDesignBasis.FromCode(
            grade, input.LocationClass, input.Joint, Temperature.FromDegreesCelsius(input.DesignTemperatureC));

        var od = Length.FromMillimeters(input.OutsideDiameterMm);
        var wall = Length.FromMillimeters(input.WallThicknessMm);
        var atm = Pressure.FromKilopascals(input.AtmosphericKPa);
        var operating = Pressure.FromMegapascals(input.OperatingPressureMPa);

        PipeDesignCheck check = SteelPipeDesign.Check(od, wall, basis, operating, atm);
        Length required = SteelPipeDesign.RequiredWallThickness(od, SteelPipeDesign.ToGauge(operating, atm), basis);

        return new MechanicsAnalysis
        {
            Grade = grade.Name,
            SmysMPa = basis.Smys.Megapascals,
            DesignFactor = basis.DesignFactor,
            JointFactor = basis.JointFactor,
            TemperatureFactor = basis.TemperatureFactor,
            AllowableHoopMPa = basis.AllowableHoopStress.Megapascals,
            DesignPressureGaugeMPa = check.DesignPressureGauge.Megapascals,
            DesignPressureAbsoluteMPa = check.DesignPressureAbsolute.Megapascals,
            OperatingGaugeMPa = check.OperatingPressureGauge.Megapascals,
            HoopStressMPa = check.OperatingHoopStress.Megapascals,
            FractionOfSmys = check.FractionOfSmys,
            DiameterToThickness = check.DiameterToThickness,
            WithinDesignPressure = check.WithinDesignPressure,
            RequiredWallMm = required.Millimeters,
            Warnings = check.Warnings
        };
    }
}
