using Evenmere.Core.Ids;
using Evenmere.Sim.Queries;
using Evenmere.Sim.Queries.Types;
using Evenmere.Sim.Tests.Fakes;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Progression.Eras;

/// <summary>
/// The gateway status hook (M2-11, per ADR-0017): advisors read which half of an era's gateway pair is done.
/// The status follows the applied effect sources, which research and culture write exactly once per completion.
/// </summary>
public sealed class GatewayStatusTests
{
    [Fact]
    public void GatewayStatus_NoGatewayNamed_ReportsNeitherDone()
    {
        var harness = ProgressionHarness.Create();
        EraId founding = ProgressionPack.Content.IdOf<EraId>("founding");

        ((IProgressionQueries)harness.Queries).GatewayStatus(founding).ShouldBe(new GatewayStatus(false, false));
    }

    [Fact]
    public void GatewayStatus_UnknownEra_ReportsNeitherDone()
    {
        var harness = ProgressionHarness.Create();

        ((IProgressionQueries)harness.Queries).GatewayStatus(EraId.From(ushort.MaxValue)).ShouldBe(new GatewayStatus(false, false));
    }

    [Fact]
    public void GatewayStatus_TracksEachGatewaySeparately()
    {
        var harness = ProgressionHarness.Create();
        EraId township = ProgressionPack.Content.IdOf<EraId>("township");

        IProgressionQueries queries = harness.Queries;
        queries.GatewayStatus(township).ShouldBe(new GatewayStatus(false, false));

        harness.ApplyTech("town_charter");
        queries.GatewayStatus(township).ShouldBe(new GatewayStatus(true, false));

        harness.Apply(EffectSource.ForCivic(ProgressionPack.Content.IdOf<CivicId>("village_moot")));
        queries.GatewayStatus(township).ShouldBe(new GatewayStatus(true, true));
    }

    [Fact]
    public void GatewayStatus_FakeReportsWhatTheTestSets()
    {
        var fake = new FakeProgressionQueries();
        EraId township = WorldBuilder.Content.IdOf<EraId>("township");

        ((IProgressionQueries)fake).GatewayStatus(township).ShouldBe(new GatewayStatus(false, false));

        fake.SetGateway(township, techDone: false, civicDone: true);
        ((IProgressionQueries)fake).GatewayStatus(township).ShouldBe(new GatewayStatus(false, true));
    }
}
