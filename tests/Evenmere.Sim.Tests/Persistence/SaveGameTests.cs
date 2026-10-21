using System.Text.Json;
using Evenmere.Content;
using Evenmere.Sim.Persistence;
using Evenmere.Sim.Tests.Integration;

namespace Evenmere.Sim.Tests.Persistence;

/// <summary>The save file: write-read round trips, framing errors and the JSON debug variant.</summary>
public sealed class SaveGameTests
{
    [Fact]
    public void WriteRead_SameContent_RestoresSameHashWithCleanReport()
    {
        ContentDb content = SiteFinder.Content;
        Simulation simulation = SliceHarness.RunTo(1200);

        using var stream = new MemoryStream();
        SaveGame.Write(stream, simulation, new SaveMeta(content, "test", "starter"));
        stream.Position = 0;

        (ISimulation restored, SaveReport report) = SaveGame.Read(stream, content);

        restored.ComputeStateHash().ShouldBe(simulation.ComputeStateHash());
        restored.CurrentTick.ShouldBe(1200);
        report.IsClean.ShouldBeTrue();
    }

    [Fact]
    public void Read_WrongMagic_ThrowsInvalidData()
    {
        using var stream = new MemoryStream([1, 2, 3, 4, 0, 0]);

        Should.Throw<InvalidDataException>(() => SaveGame.Read(stream, SiteFinder.Content));
    }

    [Fact]
    public void Read_Truncated_ThrowsInvalidData()
    {
        ContentDb content = SiteFinder.Content;
        Simulation simulation = SliceHarness.RunTo(120);

        using var stream = new MemoryStream();
        SaveGame.Write(stream, simulation, new SaveMeta(content, "test", "starter"));

        byte[] truncated = stream.ToArray()[..^10];
        Should.Throw<InvalidDataException>(() => SaveGame.Read(new MemoryStream(truncated), content));
    }

    [Fact]
    public void Read_CorruptBody_ThrowsInvalidData()
    {
        ContentDb content = SiteFinder.Content;
        Simulation simulation = SliceHarness.RunTo(120);

        using var stream = new MemoryStream();
        SaveGame.Write(stream, simulation, new SaveMeta(content, "test", "starter"));

        byte[] bytes = stream.ToArray();
        bytes[^1] ^= 0xFF;
        Should.Throw<InvalidDataException>(() => SaveGame.Read(new MemoryStream(bytes), content));
    }

    [Fact]
    public void ReadStateHash_MatchesSimulationHash()
    {
        ContentDb content = SiteFinder.Content;
        Simulation simulation = SliceHarness.RunTo(600);

        using var stream = new MemoryStream();
        SaveGame.Write(stream, simulation, new SaveMeta(content, "test", "starter"));
        stream.Position = 0;

        SaveGame.ReadStateHash(stream).ShouldBe(simulation.ComputeStateHash());
    }

    [Fact]
    public void WriteJson_WritesReadableHeaderAndBody()
    {
        ContentDb content = SiteFinder.Content;
        Simulation simulation = SliceHarness.RunTo(120);

        using var stream = new MemoryStream();
        SaveGame.WriteJson(stream, simulation, new SaveMeta(content, "test", "starter"));
        stream.Position = 0;

        using var document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;
        root.GetProperty("format").GetString().ShouldBe("evmr-json-1");
        root.GetProperty("tick").GetInt64().ShouldBe(120);
        root.GetProperty("mapId").GetString().ShouldBe("starter");
        root.GetProperty("goods").EnumerateArray().ShouldContain(e => e.GetString() == "bread");
        root.GetProperty("body").ValueKind.ShouldBe(JsonValueKind.Array);
    }
}
