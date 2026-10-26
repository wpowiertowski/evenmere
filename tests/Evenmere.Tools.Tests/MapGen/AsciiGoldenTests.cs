using Evenmere.Content.Maps;
using Evenmere.Sim.Systems.Map.Generation;
using Evenmere.Tools.MapGen;

namespace Evenmere.Tools.Tests.MapGen;

/// <summary>The ASCII grid for seed 1 is the committed contract for agents: the generator must reproduce it exactly.</summary>
public sealed class AsciiGoldenTests
{
    [Fact]
    public void ToAscii_Seed1_MatchesGoldenFile()
    {
        MapDef map = IslandGenerator.Generate(1, 320, BotWorld.Content);

        string ascii = MapGenRenderer.ToAscii(map, BotWorld.Content);
        string golden = File.ReadAllText(GoldenPath()).Replace("\r\n", "\n", StringComparison.Ordinal);

        ascii.ShouldBe(golden);
    }

    private static string GoldenPath()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Evenmere.sln")))
            {
                return Path.Combine(directory.FullName, "tests", "Evenmere.Tools.Tests", "MapGen", "Seed1Ascii.golden.txt");
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Evenmere.sln was not found above " + AppContext.BaseDirectory);
    }
}
