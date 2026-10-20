namespace Evenmere.Content.Tests;

public sealed class ScaffoldTests
{
    [Fact]
    public void Pipeline_RunsTests_Passes()
    {
        (1 + 1).ShouldBe(2);
    }
}
