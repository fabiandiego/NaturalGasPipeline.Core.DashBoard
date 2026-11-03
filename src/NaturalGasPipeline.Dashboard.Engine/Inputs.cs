using NaturalGasPipeline.Compression;
using NaturalGasPipeline.Core.Flow;
using NaturalGasPipeline.Hydraulics;
using NaturalGasPipeline.Mechanics;
using NaturalGasPipeline.Thermodynamics;
using UnitsNet;

namespace NaturalGasPipeline.Dashboard.Engine;

// Inputs are plain mutable classes in display units so the UI can bind to
// them directly. Display units: pressure MPa (absolute), temperature °C,
// length km / m / mm, standard flow MMm³/d (million standard m³ per day).
// Conversion to SI happens once, in the services, through UnitsNet.

public sealed class ComponentEntry
{
    public string Name { get; set; } = "";

    /// <summary>Mole percent.</summary>
    public double Percent { get; set; }
}

/// <summary>Gas composition in mole percent.</summary>
public sealed class GasDefinition
{
    public List<ComponentEntry> Components { get; set; } =
        EngineCatalog.ComponentNames.Select(n => new ComponentEntry { Name = n }).ToList();

    public double Total => Components.Sum(c => c.Percent);

    public void Normalize()
    {
        double total = Total;

        if (total <= 0.0)
        {
            return;
        }

        foreach (ComponentEntry c in Components)
        {
            c.Percent = c.Percent * 100.0 / total;
        }
    }

    public void Set(IEnumerable<(string Name, double Percent)> values)
    {
        foreach (ComponentEntry c in Components)
        {
            c.Percent = 0.0;
        }

        foreach ((string name, double percent) in values)
        {
            ComponentEntry? c = Components.FirstOrDefault(x => x.Name == name);

            if (c is null)
            {
                c = new ComponentEntry { Name = name };
                Components.Add(c);
            }

            c.Percent = percent;
        }
    }

    /// <summary>
    /// Builds the engine mixture. Components at 0 % are left out (AGA8 and the
    /// cubic equations do not need them). The engine validates the sum.
    /// </summary>
    public GasMixture ToMixture()
    {
        var mixture = new GasMixture();

        foreach (ComponentEntry c in Components.Where(c => c.Percent > 0.0))
        {
            GasComponent component = EngineCatalog.CreateComponent(c.Name);
            component.MoleFraction = c.Percent / 100.0;
            mixture.Components.Add(component);
        }

        mixture.ValidateComposition();
        return mixture;
    }

    public double Fraction(string name) =>
        (Components.FirstOrDefault(c => c.Name == name)?.Percent ?? 0.0) / 100.0;
}

/// <summary>Reference conditions of the standard volumetric flow.</summary>
public sealed class StandardConditionsInput
{
    public double TemperatureC { get; set; } = 15.0;

    public double PressureKPa { get; set; } = 101.325;

    /// <summary>
    /// Conditions with Z_std from the chosen model, as the Unity front end
    /// does (never Z = 1 silently).
    /// </summary>
    public StandardFlowConditions Build(ICompressibilityModel z)
    {
        var t = Temperature.FromDegreesCelsius(TemperatureC);
        var p = Pressure.FromKilopascals(PressureKPa);
        return new StandardFlowConditions { Temperature = t, Pressure = p, CompressibilityFactor = z.Calculate(p, t) };
    }
}

/// <summary>Friction factor choice: one of the engine's built-in correlations or AGA with a drag factor.</summary>
public sealed class FrictionChoice
{
    public FrictionFactorModelType Type { get; set; } = FrictionFactorModelType.ColebrookWhite;

    /// <summary>When true, AGA transmission factor with drag factor <see cref="DragFactor"/> is used instead of <see cref="Type"/>.</summary>
    public bool UseAgaDragFactor { get; set; }

    public double DragFactor { get; set; } = 0.95;

    public IFrictionFactorModel Create() =>
        UseAgaDragFactor ? new AgaTransmissionFactorModel(DragFactor) : FrictionFactorModelFactory.Create(Type);
}

public enum SegmentSolveMode
{
    /// <summary>Given P1 and Q, find P2.</summary>
    OutletPressure,

    /// <summary>Given P1 and P2, find Q.</summary>
    FlowRate,

    /// <summary>Given P1, P2 and Q, find the internal diameter (General Flow equation).</summary>
    InternalDiameter
}

public sealed class SegmentInput
{
    public CompressibilityModelType ZModel { get; set; } = CompressibilityModelType.Aga8Detail;

    public FrictionChoice Friction { get; set; } = new();

    public StandardConditionsInput Standard { get; set; } = new();

    public SegmentSolveMode Mode { get; set; } = SegmentSolveMode.OutletPressure;

    public double FlowingTemperatureC { get; set; } = 15.0;

    public double InletPressureMPa { get; set; } = 7.5;

    public double OutletPressureMPa { get; set; } = 5.5;

    public double FlowMMm3PerDay { get; set; } = 30.0;

    public double InternalDiameterMm { get; set; } = 1176.0;

    public double LengthKm { get; set; } = 120.0;

    public double RoughnessMm { get; set; } = 0.0457;

    public double ElevationChangeM { get; set; } = 150.0;

    /// <summary>Pipeline efficiency E of the power-law equations (Weymouth, Panhandle A/B).</summary>
    public double PipelineEfficiency { get; set; } = 0.95;

    /// <summary>Number of sub-segments for the pressure profile along the pipe.</summary>
    public int ProfileParts { get; set; } = 40;
}

public sealed class RoutePoint
{
    public double ChainageKm { get; set; }

    public double ElevationM { get; set; }
}

public enum PlacementChoice
{
    /// <summary>As the Unity front end: minimum count by maximum spacing, then equal ratios at that count.</summary>
    Automatic,
    MaximumSpacing,
    EqualRatio
}

public sealed class PlannerInput
{
    public CompressibilityModelType ZModel { get; set; } = CompressibilityModelType.Aga8Detail;

    public FrictionChoice Friction { get; set; } = new();

    public StandardConditionsInput Standard { get; set; } = new();

    public double FlowingTemperatureC { get; set; } = 15.0;

    public double FlowMMm3PerDay { get; set; } = 42.475;

    public double InletPressureMPa { get; set; } = 10.03;

    public double DeliveryPressureMPa { get; set; } = 5.617;

    /// <summary>null: equal to the inlet pressure.</summary>
    public double? StationDischargeMPa { get; set; }

    /// <summary>null: equal to the delivery pressure.</summary>
    public double? MinimumSuctionMPa { get; set; }

    /// <summary>Absolute MAOP; null: not checked.</summary>
    public double? MaopMPa { get; set; }

    /// <summary>null: not checked.</summary>
    public double? MaximumVelocityMs { get; set; }

    public double ErosionalConstant { get; set; } = 100.0;

    public double StepKm { get; set; } = 1.0;

    public double InternalDiameterMm { get; set; } = 1036.32;

    public double RoughnessMm { get; set; } = 0.0457;

    /// <summary>k = cp/cv for the isentropic station power.</summary>
    public double HeatCapacityRatio { get; set; } = 1.28;

    public PlacementChoice Placement { get; set; } = PlacementChoice.Automatic;

    public int StationCount { get; set; } = 3;

    /// <summary>How many station counts above the minimum the comparison table shows.</summary>
    public int AdditionalStations { get; set; } = 3;

    public List<RoutePoint> Route { get; set; } = new();
}

public sealed class CompressorInput
{
    public CompressibilityModelType ZModel { get; set; } = CompressibilityModelType.Aga8Detail;

    public StandardConditionsInput Standard { get; set; } = new();

    public double FlowMMm3PerDay { get; set; } = 42.475;

    public double SuctionPressureMPa { get; set; } = 5.6;

    public double SuctionTemperatureC { get; set; } = 15.0;

    public double DischargePressureMPa { get; set; } = 10.0;

    public double HeatCapacityRatio { get; set; } = 1.28;

    public CompressionEfficiencyBasis Basis { get; set; } = CompressionEfficiencyBasis.Polytropic;

    public double Efficiency { get; set; } = 0.85;

    public double MechanicalEfficiency { get; set; } = 0.98;

    public int Stages { get; set; } = 2;

    /// <summary>null: no intercooling.</summary>
    public double? IntercoolerOutletC { get; set; } = 30.0;
}

public sealed class MechanicsInput
{
    public double OutsideDiameterMm { get; set; } = 1219.2;

    public double WallThicknessMm { get; set; } = 18.4;

    public string GradeName { get; set; } = ApiLinePipeGrades.X70.Name;

    public LocationClass LocationClass { get; set; } = LocationClass.Class1Division2;

    public LongitudinalJoint Joint { get; set; } = LongitudinalJoint.SubmergedArcWelded;

    public double DesignTemperatureC { get; set; } = 50.0;

    public double OperatingPressureMPa { get; set; } = 10.0;

    public double AtmosphericKPa { get; set; } = 101.325;
}
