using NaturalGasPipeline.Dashboard.Engine;

namespace NaturalGasPipeline.Dashboard;

/// <summary>
/// Inputs shared by every tab. The gas composition is defined once and used
/// by the segment, planner and compressor calculations.
/// </summary>
public sealed class AppState
{
    public AppState()
    {
        Presets.ExampleCase r3 = Presets.Cases.Single(c => c.Id == "R3");
        Presets.Apply(r3, Planner, Gas);
        CaseId = r3.Id;
    }

    public GasDefinition Gas { get; } = new();

    public string CaseId { get; set; }

    public double GasPressureMPa { get; set; } = 7.0;

    public double GasTemperatureC { get; set; } = 15.0;

    public NaturalGasPipeline.Thermodynamics.CompressibilityModelType GasModel { get; set; } =
        NaturalGasPipeline.Thermodynamics.CompressibilityModelType.Aga8Detail;

    public StandardConditionsInput Standard { get; } = new();

    public SegmentInput Segment { get; } = new();

    public PlannerInput Planner { get; } = new();

    public CompressorInput Compressor { get; } = new();

    public MechanicsInput Mechanics { get; } = new();

    /// <summary>Raised when the gas changes, so tabs can recalculate.</summary>
    public event Action? GasChanged;

    public void NotifyGasChanged() => GasChanged?.Invoke();
}
