using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;
using Evenmere.Game.Ui.Debug;
using Evenmere.Sim;
using Evenmere.Sim.Systems.Debug.Commands;

namespace Evenmere.Game.Tests;

/// <summary>The cheat console parser: every documented line parses, and everything else explains itself.</summary>
public sealed class CheatParserTests
{
    [Fact]
    public void Parse_Grant_BuildsTheCommand()
    {
        CheatParser.TryParse("grant timber 10", TestContent.Db, out Command? command, out string error)
            .ShouldBeTrue(error);

        command.ShouldBe(new CheatGrantGoods(TestContent.Db.IdOf<GoodId>("timber"), Amount.FromUnits(10)));
    }

    [Fact]
    public void Parse_GrantFractional_BuildsTheCommand()
    {
        CheatParser.TryParse("grant timber 2.5", TestContent.Db, out Command? command, out string error)
            .ShouldBeTrue(error);

        command.ShouldBe(new CheatGrantGoods(TestContent.Db.IdOf<GoodId>("timber"), Amount.Parse("2.5")));
    }

    [Fact]
    public void Parse_GrantUnknownGood_NamesTheGood()
    {
        CheatParser.TryParse("grant unobtainium 10", TestContent.Db, out Command? command, out string error)
            .ShouldBeFalse();

        command.ShouldBeNull();
        error.ShouldContain("unobtainium");
    }

    [Fact]
    public void Parse_GrantBadAmount_ExplainsTheShape()
    {
        CheatParser.TryParse("grant timber lots", TestContent.Db, out Command? command, out string error)
            .ShouldBeFalse();

        command.ShouldBeNull();
        error.ShouldContain("decimal");
    }

    [Fact]
    public void Parse_Tech_BuildsTheCompletion()
    {
        string tech = TestContent.Db.Techs.Names[0];
        CheatParser.TryParse($"tech {tech}", TestContent.Db, out Command? command, out string error)
            .ShouldBeTrue(error);

        command.ShouldBe(new CheatCompleteNode(TestContent.Db.IdOf<TechId>(tech), null));
    }

    [Fact]
    public void Parse_Civic_BuildsTheCompletion()
    {
        string civic = TestContent.Db.Civics.Names[0];
        CheatParser.TryParse($"civic {civic}", TestContent.Db, out Command? command, out string error)
            .ShouldBeTrue(error);

        command.ShouldBe(new CheatCompleteNode(null, TestContent.Db.IdOf<CivicId>(civic)));
    }

    [Fact]
    public void Parse_Era_BuildsTheCommand()
    {
        string era = TestContent.Db.Eras.Names[0];
        CheatParser.TryParse($"era {era}", TestContent.Db, out Command? command, out string error)
            .ShouldBeTrue(error);

        command.ShouldBe(new CheatSetEra(TestContent.Db.IdOf<EraId>(era)));
    }

    [Fact]
    public void Parse_RevealAll_BuildsTheCommand()
    {
        CheatParser.TryParse("reveal all", TestContent.Db, out Command? command, out string error)
            .ShouldBeTrue(error);

        command.ShouldBe(new CheatRevealAll(CheatRevealAll.AllRegions));
    }

    [Fact]
    public void Parse_RevealRegion_BuildsTheCommand()
    {
        CheatParser.TryParse("reveal 3", TestContent.Db, out Command? command, out string error)
            .ShouldBeTrue(error);

        command.ShouldBe(new CheatRevealAll(3));
    }

    [Fact]
    public void Parse_Residents_BuildsTheCommand()
    {
        CheatParser.TryParse("residents 5", TestContent.Db, out Command? command, out string error)
            .ShouldBeTrue(error);

        command.ShouldBe(new CheatAddResidents(5));
    }

    [Fact]
    public void Parse_ResidentsZero_ExplainsTheShape()
    {
        CheatParser.TryParse("residents 0", TestContent.Db, out Command? command, out string error)
            .ShouldBeFalse();

        command.ShouldBeNull();
        error.ShouldContain("at least 1");
    }

    [Fact]
    public void Parse_UnknownLine_PrintsUsage()
    {
        CheatParser.TryParse("make it rain", TestContent.Db, out Command? command, out string error)
            .ShouldBeFalse();

        command.ShouldBeNull();
        error.ShouldContain(CheatParser.Usage);
    }

    [Fact]
    public void Parse_EmptyLine_PrintsUsage()
    {
        CheatParser.TryParse(string.Empty, TestContent.Db, out Command? command, out string error)
            .ShouldBeFalse();

        command.ShouldBeNull();
        error.ShouldContain(CheatParser.Usage);
    }
}
