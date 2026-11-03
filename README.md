# NaturalGasPipeline.Core · Dashboard

Interactive web dashboard for the natural-gas transmission calculation engine
[NaturalGasPipeline.Core](https://github.com/fabiandiego/NaturalGasPipeline.Core).
The engine itself is compiled to WebAssembly (Blazor, .NET 8) and runs in the
browser: the numbers on screen are the engine's, not a re-implementation.

**Live:** https://fabiandiego.github.io/NaturalGasPipeline.Core.DashBoard/

| Tab | Engine question | Engine module | Diagrams |
|---|---|---|---|
| Gas properties | Q1 · What gas do I have? | Thermodynamics | calculation flow, Z(P) for every model, deviation from AGA8-DC92 |
| Pipe segment | Q2 · Pressure loss A → B | Hydraulics, Correlations | segment schematic with ΔP split, P(x) profile, Moody chart with operating point |
| Station planning | Q3 · How many stations, how far apart? | Network | line diagram, hydraulic gradient, terrain, power vs station count |
| Compressor station | Q4 · What is each station like? | Compression | compression train, temperature path, power vs number of stages |
| Wall thickness | — | Mechanics (pending review) | cross-section with hoop stress, utilisation against SMYS |

Pressures are absolute unless marked (g). Standard flow is in MMm³/d at the
stated base conditions, with Z at those conditions from the selected model.

## Keeping up with the engine

The engine is a git submodule (`engine/` → `NaturalGasPipeline.Core`, branch `master`).

- **Automatic:** the workflow `build-test-deploy` runs daily, moves `engine/`
  to the latest `master`, runs the bridge tests and, only if they pass,
  commits the update and redeploys the site.
- **On demand:** GitHub → Actions → *build-test-deploy* → *Run workflow*.
- **From the engine repo (optional):** a step in the engine's CI can send a
  `repository_dispatch` event of type `core-updated` to this repository
  (needs a token with access to this repo stored as a secret there).
- **Locally:** `git submodule update --remote engine`, then build.

What adapts without code changes: components, Z models
(`CompressibilityModelType`), friction correlations (`FrictionFactorModelType`),
pipe grades (`ApiLinePipeGrades.All`), location classes, seams, efficiency
bases and `[PendingReview]` items are discovered from the engine at start-up
(`src/NaturalGasPipeline.Dashboard.Engine/EngineCatalog.cs`). A change of the
engine's public API needs a matching change in the bridge, and the build says
so before anything is deployed.

The tests (`tests/NaturalGasPipeline.Dashboard.Engine.Tests`) run the five
example cases of the engine's `RealisticCasesTests` through the dashboard's
bridge and require the same station counts (R1 0, R2 0, R3 3 — published
values; S1 8, S2 1 — engine regression values).

## Structure

```
engine/                                 git submodule: NaturalGasPipeline.Core
src/NaturalGasPipeline.Dashboard.Engine  bridge: display units ↔ engine, result records (pure C#)
src/NaturalGasPipeline.Dashboard         Blazor WebAssembly UI, SVG charts and diagrams
tests/NaturalGasPipeline.Dashboard.Engine.Tests
.github/workflows/build-deploy.yml      test, update engine, AOT publish, deploy to gh-pages
```

## Run locally

```bash
git clone --recurse-submodules https://github.com/fabiandiego/NaturalGasPipeline.Core.DashBoard.git
cd NaturalGasPipeline.Core.DashBoard
dotnet test tests/NaturalGasPipeline.Dashboard.Engine.Tests
dotnet run --project src/NaturalGasPipeline.Dashboard
```

or open `NaturalGasPipeline.Dashboard.sln` in Visual Studio 2022. Without
`-p:RunAOTCompilation=true` the engine runs interpreted, which is slower for
the station planner with AGA8.

## Visual style

Industrial gas-transmission corporate look: deep blue, white and steel greys,
engineering-drawing frames with title blocks. The mark, palette and drawings
are original; no third-party logo, name or typeface is used. Fonts: PT Sans,
PT Sans Narrow and PT Mono (ParaType, SIL Open Font License), loaded from
Google Fonts.

## License

MIT, as the engine. See [LICENSE](LICENSE).
