using NaturalGasPipeline.Thermodynamics;

namespace NaturalGasPipeline.Dashboard.Engine;

/// <summary>
/// Example cases. R1–R3 and S1–S2 are the cases shipped with the Unity front
/// end and checked by the engine test <c>RealisticCasesTests</c>
/// (engine/tests/NaturalGasPipeline.Network.Tests); the values are copied
/// verbatim from that file. R1–R3 are real pipelines with published design
/// data (TAP onshore, Medgaz, Ruby) and approximate terrain profiles; S1–S2
/// are synthetic. "Typical" is the engine's <see cref="NaturalGasMixtures.TypicalNaturalGas"/>.
/// </summary>
public static class Presets
{
    public sealed record ExampleCase(
        string Id,
        string Title,
        (string Name, double Percent)[] Composition,
        double StdTemperatureC,
        double StdPressureKPa,
        double FlowMMm3PerDay,
        double InletMPa,
        double DeliveryMPa,
        double FlowingC,
        double InternalDiameterMm,
        (double Km, double M)[] Route,
        string ExpectedStations);

    public static readonly ExampleCase[] Cases =
    {
        new("R1", "R1 · TAP onshore (Kipoi → Fier), 10 bcm/y",
            new[] { ("Methane", 92.4835), ("Ethane", 3.9409999999999985), ("Propane", 1.7645999999999993), ("IsoButane", 0.3243999999999999), ("NormalButane", 0.6871999999999998), ("Nitrogen", 0.09339999999999997), ("CarbonDioxide", 0.7058999999999997) },
            15.0, 101.325, 27.397260273972606, 9.601325, 5.0, 15.0, 1183.0,
            new[] { (0.0, 20.0), (60.0, 50.0), (150.0, 300.0), (230.0, 100.0), (300.0, 50.0), (380.0, 150.0), (450.0, 600.0), (520.0, 800.0), (550.0, 900.0), (580.0, 850.0), (620.0, 1800.0), (660.0, 900.0), (700.0, 300.0), (740.0, 50.0), (765.0, 10.0) },
            "0 intermediate stations (published)"),
        new("R2", "R2 · Medgaz (offshore), 8 bcm/y",
            new[] { ("Methane", 85.2), ("Ethane", 9.204999999999998), ("Propane", 1.4219999999999997), ("IsoButane", 0.13099999999999998), ("NormalButane", 0.2809999999999999), ("Nitrogen", 2.0849999999999995), ("CarbonDioxide", 1.6759999999999997) },
            15.0, 101.325, 21.91780821917808, 20.101325, 8.301325, 15.0, 558.8000000000001,
            new[] { (0.0, 0.0), (15.0, -150.0), (71.0, -900.0), (77.0, -1700.0), (110.0, -2155.0), (150.0, -1500.0), (175.0, -500.0), (190.0, -100.0), (198.3, 0.0) },
            "0 intermediate stations, arrival ≥ 82 barg (published)"),
        new("R3", "R3 · Ruby pipeline",
            new[] { ("Methane", 96.302), ("Ethane", 1.6379999999999997), ("Propane", 0.05899999999999999), ("IsoButane", 0.005999999999999999), ("NormalButane", 0.006999999999999999), ("Nitrogen", 1.1999999999999997), ("CarbonDioxide", 0.7879999999999999) },
            15.555555555555555, 101.55977492836996, 42.47526988800001, 10.029775502162439, 5.617130834534689, 15.0, 1036.32,
            new[] { (0.0, 2030.0), (100.0, 2100.0), (160.0, 2000.0), (250.0, 1400.0), (330.0, 1550.0), (450.0, 1800.0), (600.0, 1650.0), (750.0, 1300.0), (900.0, 1500.0), (1000.0, 1600.0), (1086.6, 1250.0) },
            "3 intermediate stations (published: Wildcat Hills, Wieland Flat, Desert Valley)"),
        new("S1", "S1 · Synthetic trunkline, 1200 km",
            new[] { ("Methane", 95.55591111822237), ("Ethane", 2.632052641052821), ("Propane", 0.5820116402328046), ("IsoButane", 0.09700194003880078), ("NormalButane", 0.14900298005960116), ("Nitrogen", 0.6470129402588052), ("CarbonDioxide", 0.3370067401348027) },
            15.0, 101.325, 90.0, 7.5, 5.1, 15.0, 1376.8,
            new[] { (0.0, 150.0), (100.0, 180.0), (200.0, 140.0), (300.0, 220.0), (400.0, 250.0), (500.0, 190.0), (600.0, 160.0), (700.0, 210.0), (800.0, 240.0), (900.0, 170.0), (1000.0, 120.0), (1100.0, 140.0), (1200.0, 100.0) },
            "8 stations (engine regression value)"),
        new("S2", "S2 · Synthetic mountain line, 400 km",
            new[] { ("Methane", 84.89), ("Ethane", 6.418), ("Propane", 2.076), ("IsoButane", 0.285), ("NormalButane", 0.667), ("Nitrogen", 4.187), ("CarbonDioxide", 1.477) },
            15.0, 101.325, 7.0, 9.5, 6.0, 15.0, 584.2,
            new[] { (0.0, 200.0), (40.0, 500.0), (80.0, 1200.0), (120.0, 2200.0), (160.0, 3200.0), (180.0, 4000.0), (200.0, 3600.0), (240.0, 2800.0), (280.0, 2600.0), (320.0, 2700.0), (360.0, 2500.0), (400.0, 2400.0) },
            "1 station (engine regression value)")
    };

    /// <summary>Gas compositions: the engine's typical gas plus the example cases.</summary>
    public static IReadOnlyList<(string Id, string Title, (string Name, double Percent)[] Composition)> Gases()
    {
        var list = new List<(string, string, (string, double)[])>();

        try
        {
            GasMixture typical = NaturalGasMixtures.TypicalNaturalGas;
            list.Add(("TYP", "Typical natural gas (engine)",
                typical.Components.Select(c => (c.Name, c.MoleFraction * 100.0)).ToArray()));
        }
        catch (Exception)
        {
            // The engine may change or drop the preset; the case gases remain.
        }

        list.AddRange(Cases.Select(c => (c.Id, c.Title, c.Composition)));
        return list;
    }

    /// <summary>Loads a case into a planner input (composition goes to <paramref name="gas"/>).</summary>
    public static void Apply(ExampleCase c, PlannerInput input, GasDefinition gas)
    {
        gas.Set(c.Composition);
        input.Standard.TemperatureC = c.StdTemperatureC;
        input.Standard.PressureKPa = c.StdPressureKPa;
        input.FlowMMm3PerDay = c.FlowMMm3PerDay;
        input.InletPressureMPa = c.InletMPa;
        input.DeliveryPressureMPa = c.DeliveryMPa;
        input.FlowingTemperatureC = c.FlowingC;
        input.InternalDiameterMm = c.InternalDiameterMm;
        input.RoughnessMm = 0.0457;
        input.HeatCapacityRatio = 1.28;
        input.StationDischargeMPa = null;
        input.MinimumSuctionMPa = null;
        input.Route = c.Route.Select(p => new RoutePoint { ChainageKm = p.Km, ElevationM = p.M }).ToList();
    }
}
