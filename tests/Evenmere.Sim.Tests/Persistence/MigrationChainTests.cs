using Evenmere.Content;
using Evenmere.Sim.Persistence;
using Evenmere.Sim.State;
using Evenmere.Sim.Tests.Fixtures;

namespace Evenmere.Sim.Tests.Persistence;

/// <summary>The migration chain: ordered steps from a version to the current format, one step per version.</summary>
public sealed class MigrationChainTests
{
    [Fact]
    public void Run_ChainOfTwo_AppliesInOrder()
    {
        ContentDb content = WorldBuilder.Content;
        var log = new List<int>();
        IMigration first = new FakeMigration(1, 2, log);
        IMigration second = new FakeMigration(2, 3, log);

        var state = WorldState.CreateNew(content, 7);
        WorldState result = MigrationChain.Run(1, 3, state, content, [second, first]);

        result.ShouldBeSameAs(state);
        log.ShouldBe([1, 2]);
    }

    [Fact]
    public void Run_CurrentVersion_RunsNothing()
    {
        ContentDb content = WorldBuilder.Content;
        var state = WorldState.CreateNew(content, 7);

        MigrationChain.Run(SaveFormat.CurrentVersion, state, content, MigrationChain.All).ShouldBeSameAs(state);
    }

    [Fact]
    public void Run_MissingStep_ThrowsInvalidData()
    {
        ContentDb content = WorldBuilder.Content;
        var state = WorldState.CreateNew(content, 7);

        Should.Throw<InvalidDataException>(() => MigrationChain.Run(0, state, content, MigrationChain.All));
    }

    [Fact]
    public void Run_TwoStepsFromSameVersion_ThrowsInvalidData()
    {
        ContentDb content = WorldBuilder.Content;
        var state = WorldState.CreateNew(content, 7);

        Should.Throw<InvalidDataException>(() =>
            MigrationChain.Run(1, 3, state, content, [new FakeMigration(1, 2, []), new FakeMigration(1, 3, [])]));
    }

    private sealed class FakeMigration(int from, int to, List<int> log) : IMigration
    {
        public int FromVersion => from;

        public int ToVersion => to;

        public WorldState Apply(WorldState state, ContentDb content)
        {
            ArgumentNullException.ThrowIfNull(state);
            ArgumentNullException.ThrowIfNull(content);
            log.Add(from);
            return state;
        }
    }
}
