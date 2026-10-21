using Evenmere.Game.Data.Visuals;
using Evenmere.Game.Rendering.Buildings;

namespace Evenmere.Game.Tests;

public sealed class BuildingMeshTests
{
    [Fact]
    public void Body_EmitsVertsAndIndices()
    {
        var builder = new BuildingMeshBuilder();
        builder.RebuildBody(BuildingShape.House, 2f, 2f);
        builder.VertexCount.ShouldBeGreaterThan(0);
        (builder.IndexCount % 3).ShouldBe(0);
    }

    [Fact]
    public void Ruin_IsLowerThanBody()
    {
        var body = new BuildingMeshBuilder();
        body.RebuildBody(BuildingShape.House, 2f, 2f);
        var ruin = new BuildingMeshBuilder();
        ruin.RebuildRuin(2f, 2f);
        MaxY(ruin).ShouldBeLessThan(MaxY(body));
    }

    [Fact]
    public void VisualMap_LoadsValidEntries()
    {
        string json = """{"buildings":{"settler_house":{"colour":"#8899AA","shape":"house","icon":"H"}}}""";
        var warnings = new List<string>();
        var map = BuildingVisualMap.Load(json, warnings.Add);
        warnings.ShouldBeEmpty();
        map.TryGet("settler_house", out BuildingVisual visual).ShouldBeTrue();
        visual.Shape.ShouldBe(BuildingShape.House);
    }

    [Fact]
    public void VisualMap_BadColour_WarnsAndSkips()
    {
        string json = """{"buildings":{"bad":{"colour":"red","shape":"house","icon":"H"},"good":{"colour":"#112233","shape":"hut","icon":"h"}}}""";
        var warnings = new List<string>();
        var map = BuildingVisualMap.Load(json, warnings.Add);
        warnings.Count.ShouldBe(1);
        map.TryGet("bad", out _).ShouldBeFalse();
        map.TryGet("good", out _).ShouldBeTrue();
    }

    [Fact]
    public void VisualMap_BadShape_WarnsAndSkips()
    {
        string json = """{"buildings":{"odd":{"colour":"#112233","shape":"castle","icon":"c"}}}""";
        var warnings = new List<string>();
        var map = BuildingVisualMap.Load(json, warnings.Add);
        warnings.Count.ShouldBe(1);
        map.Count.ShouldBe(0);
    }

    private static float MaxY(BuildingMeshBuilder builder)
    {
        System.ReadOnlySpan<float> verts = builder.Vertices;
        float max = float.MinValue;
        for (int i = 1; i < verts.Length; i += 3)
        {
            if (verts[i] > max)
            {
                max = verts[i];
            }
        }

        return max;
    }
}
