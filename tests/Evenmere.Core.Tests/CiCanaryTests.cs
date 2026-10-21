namespace Evenmere.Core.Tests;

/// <summary>A deliberately failing test for checking that CI blocks a red pull request (M1-29). Never merge this.</summary>
public sealed class CiCanaryTests
{
    [Fact]
    public void Canary_AlwaysFails_ProvesCiGoesRed() => Assert.Fail("Deliberate failure: CI must report this pull request as red.");
}
