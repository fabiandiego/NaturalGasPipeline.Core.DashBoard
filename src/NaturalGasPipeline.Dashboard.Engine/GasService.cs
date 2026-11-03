using NaturalGasPipeline.Thermodynamics;
using UnitsNet;

namespace NaturalGasPipeline.Dashboard.Engine;

/// <summary>A named curve for the charts (x, y in display units).</summary>
public sealed record Series(string Name, IReadOnlyList<(double X, double Y)> Points);

public sealed record ZRow(string Model, string Basis, double? Z, bool? WithinValidatedRange, double? DeviationFromAga8Percent, string? Error);

public sealed record GasAnalysis
{
    public required double MolarMassGPerMol { get; init; }
    public required double SpecificGravity { get; init; }
    public required string AirBasis { get; init; }
    public required double KayTpcK { get; init; }
    public required double KayPpcMPa { get; init; }
    public double? SuttonTpcK { get; init; }
    public double? SuttonPpcMPa { get; init; }
    public bool SuttonInRange { get; init; }
    public double? WichertAzizTpcK { get; init; }
    public double? WichertAzizPpcMPa { get; init; }
    public required double ReducedTemperature { get; init; }
    public required double ReducedPressure { get; init; }
    public required string SelectedModel { get; init; }
    public required double Z { get; init; }
    public required double DensityKgM3 { get; init; }
    public double? ViscosityMicroPaS { get; init; }
    public required double StandardDensityKgM3 { get; init; }
    public required double ZStandard { get; init; }
    public required IReadOnlyList<ZRow> ZTable { get; init; }
    public required IReadOnlyList<Series> ZCurves { get; init; }
    public required IReadOnlyList<Series> DeviationCurves { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }

    /// <summary>
    /// Composition part of the AGA8-DC92 normal range (a property of the gas,
    /// independent of P and T). null when the engine could not build AGA8 for
    /// this composition.
    /// </summary>
    public bool? Aga8CompositionInNormalRange { get; init; }

    public IReadOnlyList<string> Aga8CompositionViolations { get; init; } = Array.Empty<string>();
}

/// <summary>Question 1 of the engine: what gas do I have?</summary>
public static class GasService
{
    public static MolarMass AirMolarMass => DryAirMolarMass.Iso6976_2016;

    public static GasAnalysis Analyze(
        GasDefinition gas,
        double pressureMPa,
        double temperatureC,
        CompressibilityModelType selected,
        StandardConditionsInput standard,
        double curveMaxPressureMPa = 15.0)
    {
        GasMixture mixture = gas.ToMixture();
        var p = Pressure.FromMegapascals(pressureMPa);
        var t = Temperature.FromDegreesCelsius(temperatureC);
        var warnings = new List<string>();

        MolarMass m = mixture.CalculateMolarMass();
        double sg = NaturalGasPipeline.Thermodynamics.SpecificGravity.OfMixture(mixture, AirMolarMass);
        PseudoCriticalProperties kay = PseudoCriticalProperties.FromKay(mixture);

        double? suttonT = null, suttonP = null;
        bool suttonOk = false;
        try
        {
            var sutton = new SuttonPseudoCriticalCorrelation();
            PseudoCriticalProperties s = sutton.Calculate(sg);
            suttonT = s.Temperature.Kelvins;
            suttonP = s.Pressure.Megapascals;
            suttonOk = sutton.IsWithinValidatedRange(sg);
        }
        catch (Exception ex)
        {
            warnings.Add($"Sutton pseudo-criticals: {ex.Message}");
        }

        double yCo2 = gas.Fraction("CarbonDioxide");
        double yH2s = gas.Fraction("HydrogenSulfide");
        double? waT = null, waP = null;
        PseudoCriticalProperties? wa = null;
        if (yCo2 + yH2s > 0.0)
        {
            wa = WichertAzizCorrection.Apply(kay, yCo2, yH2s);
            waT = wa.Temperature.Kelvins;
            waP = wa.Pressure.Megapascals;
        }

        // State with the selected model, through the engine's GasState.
        ICompressibilityModel model = CompressibilityModelFactory.Create(selected, mixture);
        var state = new GasState { Pressure = p, Temperature = t, Mixture = mixture };
        state.CalculateState(model);
        double? mu = null;
        try
        {
            state.CalculateViscosity(new LeeGonzalezEakinViscosityModel());
            mu = state.Viscosity.MicropascalSeconds;
        }
        catch (Exception ex)
        {
            warnings.Add($"Viscosity (Lee-Gonzalez-Eakin): {ex.Message}");
        }

        if (model is ICompressibilityCorrelation corr && !corr.IsWithinValidatedRange(p, t))
        {
            warnings.Add($"{corr.Name} is outside its validated range at {pressureMPa:F2} MPa, {temperatureC:F1} °C.");
        }

        // Standard conditions.
        StandardFlowConditionsResult std = StandardState(model, m, standard);

        // Every Z model the engine offers, at (P, T).
        var models = BuildAllModels(mixture, sg, kay, wa);

        // AGA8 normal range, composition part: checked once per gas, not per (P, T).
        bool? aga8Composition = null;
        IReadOnlyList<string> aga8Violations = Array.Empty<string>();
        if (models.Select(x => x.Model).OfType<Aga8DetailModel>().FirstOrDefault() is { } agaModel)
        {
            aga8Composition = agaModel.CompositionWithinNormalRange(out aga8Violations);
            if (aga8Composition == false)
            {
                warnings.Add("Composition is outside the AGA8-DC92 normal range (the stated ≈0.1 % uncertainty in Z does not apply): "
                             + string.Join("; ", aga8Violations));
            }
        }
        double? zAga = null;
        foreach ((string name, string basis, ICompressibilityModel zm) in models)
        {
            if (zm is Aga8DetailModel)
            {
                try { zAga = zm.Calculate(p, t); } catch { zAga = null; }
            }
        }

        var table = new List<ZRow>();
        foreach ((string name, string basis, ICompressibilityModel zm) in models)
        {
            try
            {
                double z = zm.Calculate(p, t);
                bool? range = zm is ICompressibilityCorrelation c ? c.IsWithinValidatedRange(p, t) : null;
                double? dev = zAga is double za && zm is not Aga8DetailModel ? (z - za) / za * 100.0 : null;
                table.Add(new ZRow(name, basis, z, range, dev, null));
            }
            catch (Exception ex)
            {
                table.Add(new ZRow(name, basis, null, null, null, ex.Message));
            }
        }

        // Z(P) isotherm at T for every model, and deviation from AGA8.
        var curves = new List<Series>();
        var deviations = new List<Series>();
        Aga8DetailModel? aga = models.Select(x => x.Model).OfType<Aga8DetailModel>().FirstOrDefault();
        int n = 60;
        foreach (var (name, _, zm) in models)
        {
            var pts = new List<(double, double)>(n);
            var dpts = new List<(double, double)>(n);
            for (int i = 0; i <= n; i++)
            {
                double pm = 0.1 + (curveMaxPressureMPa - 0.1) * i / n;
                var pp = Pressure.FromMegapascals(pm);
                try
                {
                    double z = zm.Calculate(pp, t);
                    if (!double.IsFinite(z))
                    {
                        continue;
                    }

                    pts.Add((pm, z));
                    if (aga is not null && zm is not Aga8DetailModel && zm is not IdealGasModel)
                    {
                        double za = aga.Calculate(pp, t);
                        dpts.Add((pm, (z - za) / za * 100.0));
                    }
                }
                catch
                {
                    // A model may not converge at some points; the curve just has a gap.
                }
            }

            curves.Add(new Series(name, pts));
            if (dpts.Count > 0)
            {
                deviations.Add(new Series(name, dpts));
            }
        }

        return new GasAnalysis
        {
            MolarMassGPerMol = m.GramsPerMole,
            SpecificGravity = sg,
            AirBasis = "ISO 6976:2016 dry air, 28.96546 g/mol",
            KayTpcK = kay.Temperature.Kelvins,
            KayPpcMPa = kay.Pressure.Megapascals,
            SuttonTpcK = suttonT,
            SuttonPpcMPa = suttonP,
            SuttonInRange = suttonOk,
            WichertAzizTpcK = waT,
            WichertAzizPpcMPa = waP,
            ReducedTemperature = kay.ReducedTemperature(t),
            ReducedPressure = kay.ReducedPressure(p),
            SelectedModel = EngineCatalog.ModelName(model, selected),
            Z = state.CompressibilityFactor,
            DensityKgM3 = state.Density.KilogramsPerCubicMeter,
            ViscosityMicroPaS = mu,
            StandardDensityKgM3 = std.Density,
            ZStandard = std.Z,
            ZTable = table,
            ZCurves = curves,
            DeviationCurves = deviations,
            Warnings = warnings,
            Aga8CompositionInNormalRange = aga8Composition,
            Aga8CompositionViolations = aga8Violations
        };
    }

    private readonly record struct StandardFlowConditionsResult(double Z, double Density);

    private static StandardFlowConditionsResult StandardState(ICompressibilityModel model, MolarMass m, StandardConditionsInput standard)
    {
        var c = standard.Build(model);
        double rho = c.Pressure.Pascals * m.KilogramsPerMole /
                     (c.CompressibilityFactor * NaturalGasPipeline.Core.Common.PhysicalConstants.MolarGasConstant * c.Temperature.Kelvins);
        return new StandardFlowConditionsResult(c.CompressibilityFactor, rho);
    }

    /// <summary>
    /// Every Z model of the engine: the composition-based ones of
    /// <see cref="CompressibilityModelFactory"/> (Kay pseudo-criticals), plus
    /// the gravity-based routes (Sutton, Wichert-Aziz, CNGA).
    /// </summary>
    public static List<(string Name, string Basis, ICompressibilityModel Model)> BuildAllModels(
        GasMixture mixture, double sg, PseudoCriticalProperties kay, PseudoCriticalProperties? wichertAziz)
    {
        var list = new List<(string, string, ICompressibilityModel)>();

        foreach (CompressibilityModelType type in EngineCatalog.CompressibilityModels)
        {
            try
            {
                ICompressibilityModel zm = CompressibilityModelFactory.Create(type, mixture);
                string basis = type switch
                {
                    CompressibilityModelType.IdealGas => "Z = 1",
                    CompressibilityModelType.PengRobinson or CompressibilityModelType.SoaveRedlichKwong => "Cubic EOS, composition",
                    CompressibilityModelType.Aga8Detail => "Reference EOS, composition",
                    _ => "Corresponding states, Kay"
                };
                list.Add((EngineCatalog.ModelName(zm, type), basis, zm));
            }
            catch
            {
                // A model the engine cannot build for this composition is skipped.
            }
        }

        if (wichertAziz is not null)
        {
            list.Add(("DAK · Wichert-Aziz", "Kay + Wichert-Aziz",
                CompressibilityModelFactory.Create(CorrespondingStatesModelType.DranchukAbouKassem, wichertAziz)));
        }

        try
        {
            list.Add(("DAK · Sutton γ", "Sutton pseudo-criticals from γ",
                CompressibilityModelFactory.CreateFromSpecificGravity(
                    CorrespondingStatesModelType.DranchukAbouKassem, sg, new SuttonPseudoCriticalCorrelation())));
        }
        catch
        {
            // γ outside what the engine accepts.
        }

        try
        {
            list.Add(("CNGA", "γ only", new CngaCompressibilityModel(sg, Pressure.FromKilopascals(101.325))));
        }
        catch
        {
            // γ outside what the engine accepts.
        }

        return list;
    }
}
