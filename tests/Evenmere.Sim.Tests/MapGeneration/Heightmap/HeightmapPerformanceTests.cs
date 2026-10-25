using System.Diagnostics;
using System.Text.RegularExpressions;
using Evenmere.Sim.Systems.Map.Generation;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.MapGeneration.Heightmap;

/// <summary>The M2-01 performance and purity gates: a 320-tile island generates in well under 500 ms, and the
/// generation folder holds no floating-point <c>Math</c> call, float/double/decimal use, or framework RNG.</summary>
public sealed class HeightmapPerformanceTests
{
    [Fact]
    public void GenerateHeightmap_320_GeneratesWithinBudget()
    {
        // Warm up once so JIT and content loading never pollute the measurement.
        IslandGenerator.GenerateHeightmap(1, 320, HeightmapFixture.Content);

        var watch = Stopwatch.StartNew();
        IslandGenerator.GenerateHeightmap(2, 320, HeightmapFixture.Content);
        watch.Stop();

        watch.ElapsedMilliseconds.ShouldBeLessThan(500, "the card budgets 500 ms for a 320-tile island on the development Mac");
    }

    [Fact]
    public void GenerationFolder_HasNoFloatingPointMathOrFrameworkRandomness()
    {
        string folder = Path.Combine(WorldBuilder.RepoRoot, "src", "Evenmere.Sim", "Systems", "Map", "Generation");
        string[] files = Directory.GetFiles(folder, "*.cs");
        files.ShouldNotBeEmpty("the generator lives in Systems/Map/Generation");

        var banned = new Regex(
            @"Math\.(Sin|Cos|Tan|Asin|Acos|Atan|Atan2|Sinh|Cosh|Tanh|Exp|Log|Log10|Pow|Sqrt|Cbrt|Floor|Ceiling|Round|Truncate|IEEERemainder|FusedMultiplyAdd|PI|E|Tau)\b"
            + @"|\bSystem\.Random\b|\bnew\s+Random\s*\("
            + @"|\bfloat\b|\bdouble\b|\bdecimal\b",
            RegexOptions.None);

        var violations = new List<string>();
        foreach (string file in files)
        {
            foreach (Match match in banned.Matches(File.ReadAllText(file)))
            {
                violations.Add($"{Path.GetFileName(file)}: {match.Value}");
            }
        }

        violations.ShouldBeEmpty("generation is integer-only per ADR-0003 (Math.Min/Max/Abs on integers stay allowed)");
    }
}
