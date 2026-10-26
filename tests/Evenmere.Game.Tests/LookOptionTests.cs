using Evenmere.Game.Rendering.Look;

namespace Evenmere.Game.Tests;

/// <summary>M2-29: <c>--look=classic</c> selects the M1 look in any build; anything else (or nothing) runs the
/// hand-drawn default. The parser is pure, so these tests need no engine.</summary>
public sealed class LookOptionTests
{
    [Fact]
    public void Parse_NoFlag_IsHandDrawn()
    {
        LookOption.Parse([]).ShouldBe(LookMode.HandDrawn);
    }

    [Fact]
    public void Parse_ClassicFlag_IsClassic()
    {
        LookOption.Parse(["--seed=42", "--look=classic"]).ShouldBe(LookMode.Classic);
    }

    [Fact]
    public void Parse_HandDrawnFlag_IsHandDrawn()
    {
        LookOption.Parse(["--look=handdrawn"]).ShouldBe(LookMode.HandDrawn);
    }

    [Fact]
    public void Parse_ClassicFlag_IgnoresCase()
    {
        LookOption.Parse(["--look=Classic"]).ShouldBe(LookMode.Classic);
    }

    [Fact]
    public void Parse_UnknownLook_Throws()
    {
        Should.Throw<FormatException>(() => LookOption.Parse(["--look=sepia"]));
    }

    [Fact]
    public void Parse_UnrelatedFlags_AreIgnored()
    {
        LookOption.Parse(["--seed=42", "--map=starter", "--smoke-ticks=100"]).ShouldBe(LookMode.HandDrawn);
    }
}
