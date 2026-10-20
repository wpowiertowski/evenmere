using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Evenmere.Content.Lint;
using Evenmere.Content.Tests.Support;

namespace Evenmere.Content.Tests.Lint;

/// <summary>EVL003 beyond the committed fixtures: cycle shapes, scale and the downstream-of-a-cycle report.</summary>
public sealed class TreeRuleTests
{
    [Fact]
    public void Run_TwoSeparateCycles_ReportsBoth()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            Tech(content, "loop_a", "loop_b");
            Tech(content, "loop_b", "loop_a");
            Tech(content, "ring_x", "ring_y");
            Tech(content, "ring_y", "ring_x");
        });

        diagnostics.WithCode(LintCodes.Evl003).ShouldBeExactly(
            Expected.Error(LintCodes.Evl003, "techs/loop_a.json", "/requires/0", "loop_a -> loop_b -> loop_a"),
            Expected.Error(LintCodes.Evl003, "techs/ring_x.json", "/requires/0", "ring_x -> ring_y -> ring_x"));
    }

    [Fact]
    public void Run_NodeWhoseFirstRequirementLeavesItsCycle_StillReportsThatCycle()
    {
        // a requires c first, but c is in another cycle; a and b form a cycle of their own.
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            Tech(content, "cyc_a", "cyc_c", "cyc_b");
            Tech(content, "cyc_b", "cyc_a");
            Tech(content, "cyc_c", "cyc_d");
            Tech(content, "cyc_d", "cyc_c");
        });

        diagnostics.WithCode(LintCodes.Evl003).ShouldBeExactly(
            Expected.Error(LintCodes.Evl003, "techs/cyc_a.json", "/requires/1", "cyc_a -> cyc_b -> cyc_a"),
            Expected.Error(LintCodes.Evl003, "techs/cyc_c.json", "/requires/0", "cyc_c -> cyc_d -> cyc_c"));
    }

    [Fact]
    public void Run_TheShortestWayAroundTheCycleIsReported()
    {
        // a -> b -> c -> a is the long way; a -> c -> a is shorter.
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            Tech(content, "short_a", "short_b", "short_c");
            Tech(content, "short_b", "short_c");
            Tech(content, "short_c", "short_a");
        });

        diagnostics.WithCode(LintCodes.Evl003).ShouldBeExactly(
            Expected.Error(LintCodes.Evl003, "techs/short_a.json", "/requires/1", "short_a -> short_c -> short_a"));
    }

    [Fact]
    public void Run_NodeBehindACycleThroughAnotherRequirement_IsReportedAtTheBlockedRequirement()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            Tech(content, "ring_a", "ring_b");
            Tech(content, "ring_b", "ring_a");
            Tech(content, "behind", "net_weaving", "ring_a");
        });

        diagnostics.WithCode(LintCodes.Evl003).ShouldBeExactly(
            Expected.Error(LintCodes.Evl003, "techs/behind.json", "/requires/1", "'behind' can never be reached"),
            Expected.Error(LintCodes.Evl003, "techs/ring_a.json", "/requires/0", "ring_a -> ring_b -> ring_a"));
    }

    [Fact]
    public void Run_DiamondAndRepeatedRequirements_AreNotCycles()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            Tech(content, "top", "left", "right", "left");
            Tech(content, "left", "base");
            Tech(content, "right", "base");
            Tech(content, "base");
        });

        diagnostics.WithCode(LintCodes.Evl003).ShouldBeEmpty(DiagnosticAssertions.Dump(diagnostics));
    }

    [Fact]
    public void Run_TheSameCycleInTheCivicsTree_IsReportedAsACivicCycle()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            content.Write("civics/loop_a.json", """{ "id": "loop_a", "era": 1, "cost": 10, "requires": ["loop_b"] }""");
            content.Write("civics/loop_b.json", """{ "id": "loop_b", "era": 1, "cost": 10, "requires": ["loop_a"] }""");
        });

        diagnostics.WithCode(LintCodes.Evl003).ShouldBeExactly(
            Expected.Error(LintCodes.Evl003, "civics/loop_a.json", "/requires/0", "civic cycle: loop_a -> loop_b -> loop_a"));
    }

    [Fact]
    public void Run_ALongChain_IsNotACycleAndDoesNotOverflowTheStack()
    {
        const int Length = 3000;
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            Tech(content, Name(0));
            for (int i = 1; i < Length; i++)
            {
                Tech(content, Name(i), Name(i - 1));
            }
        });

        diagnostics.WithCode(LintCodes.Evl003).ShouldBeEmpty(DiagnosticAssertions.Dump(diagnostics.WithCode(LintCodes.Evl003)));
    }

    [Fact]
    public void Run_ALongRing_ReportsOneCycleThroughEveryNode()
    {
        const int Length = 2000;
        ImmutableArray<ContentDiagnostic> diagnostics = LintVariants.Lint(content =>
        {
            for (int i = 0; i < Length; i++)
            {
                Tech(content, Name(i), Name((i + 1) % Length));
            }
        });

        ContentDiagnostic cycle = diagnostics.WithCode(LintCodes.Evl003).Single();
        cycle.File.ShouldBe("techs/chain0000.json");
        cycle.JsonPointer.ShouldBe("/requires/0");
        cycle.Message.Split("->").Length.ShouldBe(Length + 1);
    }

    private static string Name(int index) => "chain" + index.ToString("0000", CultureInfo.InvariantCulture);

    /// <summary>Writes an era-1 tech that requires the given techs, plus the English text that EVL007 asks for.</summary>
    private static void Tech(TempContent content, string id, params string[] requires)
    {
        string list = string.Join(", ", requires.Select(static r => $"\"{r}\""));
        content.Write($"techs/{id}.json", $$"""{ "id": "{{id}}", "era": 1, "cost": 10, "requires": [{{list}}] }""");

        // One locale file per tech keeps a test that writes thousands of them from rewriting a growing file.
        var text = new StringBuilder();
        text.Append("{ \"tech.").Append(id).Append(".name\": \"").Append(id).Append("\", \"tech.").Append(id).Append(".desc\": \"A tech.\" }");
        content.Write($"locale/en/tech_{id}.json", text.ToString());
    }
}
