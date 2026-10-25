using Evenmere.Content;
using Evenmere.Content.Conditions;
using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;
using Evenmere.Sim.Commands;
using Evenmere.Sim.Tests.Progression;

namespace Evenmere.Sim.Tests.Progression.Conditions;

/// <summary>Exercises the test-only inspiration dump command: every node with an inspiration is listed with its live status.</summary>
public sealed class InspirationDumpTests
{
    [Fact]
    public void Dump_ListsEveryNodeWithAnInspiration_WithItsLiveStatus()
    {
        var harness = ProgressionHarness.Create();
        List<string> lines = Send(harness);

        int expected = CountInspirations(harness.Content);
        lines.Count.ShouldBe(expected);
        lines.ShouldAllBe(static line => line.StartsWith("tech ", StringComparison.Ordinal) || line.StartsWith("civic ", StringComparison.Ordinal));

        // Each line's status matches a direct evaluation of the same node.
        foreach (string line in lines)
        {
            bool tech = line.StartsWith("tech ", StringComparison.Ordinal);
            string name = line.Split([' ', ':'])[1];
            Condition when = tech
                ? harness.Content.Get(harness.Content.IdOf<TechId>(name)).Inspiration!
                : harness.Content.Get(harness.Content.IdOf<CivicId>(name)).Inspiration!;
            line.ShouldContain($"holds={(harness.Queries.Evaluate(when) ? "yes" : "no")}");
        }
    }

    [Fact]
    public void Dump_RunsOverTheSliceContent()
    {
        var harness = ProgressionHarness.Create(ProgressionPack.Real);
        List<string> lines = Send(harness);

        lines.ShouldNotBeEmpty("the slice techs and civics carry inspirations");
        lines.Count.ShouldBe(CountInspirations(harness.Content));
    }

    private static List<string> Send(ProgressionHarness harness)
    {
        var lines = new List<string>();
        var router = new CommandRouter();
        var queue = new CommandQueue();
        router.Register(new DumpInspirationStatusHandler(lines));
        queue.Enqueue(new DumpInspirationStatus());
        router.ApplyQueued(queue, harness.Context);
        return lines;
    }

    private static int CountInspirations(ContentDb content)
    {
        int count = 0;
        foreach (TechDef tech in content.Techs.Items)
        {
            if (tech.Inspiration is not null)
            {
                count++;
            }
        }

        foreach (CivicDef civic in content.Civics.Items)
        {
            if (civic.Inspiration is not null)
            {
                count++;
            }
        }

        return count;
    }
}
