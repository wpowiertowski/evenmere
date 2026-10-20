using Evenmere.Core.Ids;

namespace Evenmere.Core.Tests.Ids;

public sealed class ContentIdTests
{
    [Fact]
    public void GoodId_Value_RoundTripsThroughFrom()
    {
        CheckRoundTrip<GoodId>("GoodId");
    }

    [Fact]
    public void BuildingTypeId_Value_RoundTripsThroughFrom()
    {
        CheckRoundTrip<BuildingTypeId>("BuildingTypeId");
    }

    [Fact]
    public void RecipeId_Value_RoundTripsThroughFrom()
    {
        CheckRoundTrip<RecipeId>("RecipeId");
    }

    [Fact]
    public void TierId_Value_RoundTripsThroughFrom()
    {
        CheckRoundTrip<TierId>("TierId");
    }

    [Fact]
    public void TechId_Value_RoundTripsThroughFrom()
    {
        CheckRoundTrip<TechId>("TechId");
    }

    [Fact]
    public void CivicId_Value_RoundTripsThroughFrom()
    {
        CheckRoundTrip<CivicId>("CivicId");
    }

    [Fact]
    public void TraditionId_Value_RoundTripsThroughFrom()
    {
        CheckRoundTrip<TraditionId>("TraditionId");
    }

    [Fact]
    public void FestivalId_Value_RoundTripsThroughFrom()
    {
        CheckRoundTrip<FestivalId>("FestivalId");
    }

    [Fact]
    public void GreatWorkTypeId_Value_RoundTripsThroughFrom()
    {
        CheckRoundTrip<GreatWorkTypeId>("GreatWorkTypeId");
    }

    [Fact]
    public void MonumentId_Value_RoundTripsThroughFrom()
    {
        CheckRoundTrip<MonumentId>("MonumentId");
    }

    [Fact]
    public void HappeningId_Value_RoundTripsThroughFrom()
    {
        CheckRoundTrip<HappeningId>("HappeningId");
    }

    [Fact]
    public void EraId_Value_RoundTripsThroughFrom()
    {
        CheckRoundTrip<EraId>("EraId");
    }

    [Fact]
    public void TerrainId_Value_RoundTripsThroughFrom()
    {
        CheckRoundTrip<TerrainId>("TerrainId");
    }

    [Fact]
    public void Assembly_ContentIdTypes_AreExactlyTheThirteenKinds()
    {
        string[] found = typeof(GoodId).Assembly.GetTypes()
            .Where(t => t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IContentId<>)))
            .Select(t => t.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        found.ShouldBe(
        [
            "BuildingTypeId", "CivicId", "EraId", "FestivalId", "GoodId", "GreatWorkTypeId", "HappeningId",
            "MonumentId", "RecipeId", "TechId", "TerrainId", "TierId", "TraditionId",
        ]);
    }

    [Fact]
    public void Default_IsIndexZero()
    {
        default(GoodId).Value.ShouldBe((ushort)0);
        GoodId.From(0).ShouldBe(default);
    }

    [Fact]
    public void Equality_DifferentValues_AreNotEqual()
    {
        GoodId.From(3).ShouldNotBe(GoodId.From(4));
        (GoodId.From(3) == GoodId.From(3)).ShouldBeTrue();
        (GoodId.From(3) != GoodId.From(4)).ShouldBeTrue();
    }

    [Fact]
    public void ToString_Id_NamesTypeAndValue()
    {
        GoodId.From(3).ToString().ShouldBe("GoodId(3)");
        TerrainId.From(65_535).ToString().ShouldBe("TerrainId(65535)");
    }

    [Fact]
    public void GenericCode_OverIContentId_WorksForAnyIdType()
    {
        Describe(GoodId.From(2)).ShouldBe("GoodId(2)+1=GoodId(3)");
        Describe(EraId.From(0)).ShouldBe("EraId(0)+1=EraId(1)");
    }

    private static void CheckRoundTrip<TId>(string typeName)
        where TId : struct, IContentId<TId>
    {
        foreach (ushort raw in new ushort[] { 0, 1, 7, 255, 256, ushort.MaxValue })
        {
            TId id = TId.From(raw);
            id.Value.ShouldBe(raw);
            TId.From(id.Value).ShouldBe(id);
            id.ToString().ShouldBe($"{typeName}({raw})");
        }

        typeof(TId).Name.ShouldBe(typeName);
    }

    private static string Describe<TId>(TId id)
        where TId : struct, IContentId<TId>
    {
        TId next = TId.From((ushort)(id.Value + 1));
        return $"{id}+1={next}";
    }
}
