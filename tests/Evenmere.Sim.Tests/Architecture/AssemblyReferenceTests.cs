using System.Reflection;
using Evenmere.Content;
using Evenmere.Core.Ids;

namespace Evenmere.Sim.Tests.Architecture;

/// <summary>ADR-0002: which assemblies Core, Content and Sim may reference.</summary>
public sealed class AssemblyReferenceTests
{
    private static readonly Assembly _core = typeof(EntityId).Assembly;
    private static readonly Assembly _content = typeof(ContentDb).Assembly;
    private static readonly Assembly _sim = typeof(Simulation).Assembly;
    private static readonly Assembly _thisTests = typeof(AssemblyReferenceTests).Assembly;

    public static TheoryData<string> LayerNames() => ["Evenmere.Core", "Evenmere.Content", "Evenmere.Sim"];

    [Fact]
    public void Core_References_AreTheBaseClassLibraryAndSystemIoHashingOnly()
    {
        AssemblyReferenceRules.Check(_core, AssemblyReferenceRules.Core).ShouldBeEmpty();
    }

    [Fact]
    public void Content_References_AreCoreAmongEvenmereAssembliesAndNeitherGodotNorMessagePack()
    {
        AssemblyReferenceRules.Check(_content, AssemblyReferenceRules.Content).ShouldBeEmpty();
    }

    [Fact]
    public void Sim_References_AreContentAndCoreAmongEvenmereAssembliesAndNotGodot()
    {
        AssemblyReferenceRules.Check(_sim, AssemblyReferenceRules.Sim).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(LayerNames))]
    public void Layer_References_NeverIncludeGodotOrTheGameAssembly(string layer)
    {
        Assembly assembly = layer switch
        {
            "Evenmere.Core" => _core,
            "Evenmere.Content" => _content,
            "Evenmere.Sim" => _sim,
            _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, "Unknown layer."),
        };

        assembly.GetName().Name.ShouldBe(layer);
        AssemblyReferenceRules.Check(assembly, AssemblyReferenceRules.NoEngine).ShouldBeEmpty();
    }

    [Fact]
    public void ReferencedNames_RealAssemblies_ShowTheReferencesTheRulesAreAbout()
    {
        // The rules above are only meaningful if the reflection really sees these references.
        AssemblyReferenceRules.ReferencedNames(_core).ShouldContain("System.IO.Hashing");
        AssemblyReferenceRules.ReferencedNames(_content).ShouldContain("Evenmere.Core");
        AssemblyReferenceRules.ReferencedNames(_sim).ShouldContain("Evenmere.Core");
        AssemblyReferenceRules.ReferencedNames(_sim).ShouldContain("Evenmere.Content");
        AssemblyReferenceRules.ReferencedNames(_sim).ShouldContain("MessagePack");
    }

    [Fact]
    public void FrameworkAssemblies_BaseClassLibrary_IsInAndPackagesAreOut()
    {
        FrameworkAssemblies.Names.ShouldContain("System.Runtime");
        FrameworkAssemblies.Names.ShouldContain("System.Collections");
        FrameworkAssemblies.Names.ShouldContain("System.Text.Json");
        FrameworkAssemblies.Names.ShouldNotContain("System.IO.Hashing");
        FrameworkAssemblies.Names.ShouldNotContain("MessagePack");
        FrameworkAssemblies.Names.ShouldNotContain("Evenmere.Core");
        FrameworkAssemblies.Contains(typeof(object).Assembly).ShouldBeTrue();
        FrameworkAssemblies.Contains(_core).ShouldBeFalse();
    }

    [Fact]
    public void Policies_EveryLayer_BanTheEngineAndTheGameAssembly()
    {
        AssemblyReferencePolicy[] policies =
            [AssemblyReferenceRules.Core, AssemblyReferenceRules.Content, AssemblyReferenceRules.Sim, AssemblyReferenceRules.NoEngine];

        foreach (AssemblyReferencePolicy policy in policies)
        {
            policy.BannedPrefixes.ShouldContain("Godot");
            policy.BannedPrefixes.ShouldContain("Evenmere.Game");
        }
    }

    // Names no assembly in this repository references yet, so the rules are shown firing on realistic input.
    [Theory]
    [InlineData("Core", "GodotSharp", false)]
    [InlineData("Content", "GodotSharp", false)]
    [InlineData("Sim", "GodotSharp", false)]
    [InlineData("Sim", "GodotSharpEditor", false)]
    [InlineData("Core", "Evenmere.Game", false)]
    [InlineData("Content", "Evenmere.Game", false)]
    [InlineData("Sim", "Evenmere.Game", false)]
    [InlineData("Core", "Evenmere.Content", false)]
    [InlineData("Core", "Evenmere.Sim", false)]
    [InlineData("Content", "Evenmere.Sim", false)]
    [InlineData("Sim", "Evenmere.Tools", false)]
    [InlineData("Core", "MessagePack", false)]
    [InlineData("Core", "MessagePack.Annotations", false)]
    [InlineData("Content", "MessagePack", false)]
    [InlineData("Core", "JsonSchema.Net", false)]
    [InlineData("Core", "System.Text.Json.Extensions", false)]
    [InlineData("Core", "System.CommandLine", false)]
    [InlineData("Core", "Microsoft.Extensions.Logging", false)]
    [InlineData("Core", "System.Runtime", true)]
    [InlineData("Core", "System.Collections", true)]
    [InlineData("Core", "System.IO.Hashing", true)]
    [InlineData("Content", "Evenmere.Core", true)]
    [InlineData("Content", "JsonSchema.Net", true)]
    [InlineData("Content", "System.Text.Json", true)]
    [InlineData("Sim", "Evenmere.Core", true)]
    [InlineData("Sim", "Evenmere.Content", true)]
    [InlineData("Sim", "MessagePack", true)]
    public void CheckNames_ReferencedName_IsJudgedByTheLayersPolicy(string layer, string referenced, bool allowed)
    {
        AssemblyReferencePolicy policy = layer switch
        {
            "Core" => AssemblyReferenceRules.Core,
            "Content" => AssemblyReferenceRules.Content,
            "Sim" => AssemblyReferenceRules.Sim,
            _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, "Unknown layer."),
        };

        IReadOnlyList<string> violations = AssemblyReferenceRules.CheckNames($"Evenmere.{layer}", [referenced], policy);

        (violations.Count == 0).ShouldBe(allowed, string.Join("; ", violations));
    }

    [Fact]
    public void Check_AssemblyWithOtherEvenmereAndPackageReferences_FailsTheCorePolicy()
    {
        // The test assembly references Sim, Content, Core, MessagePack, Shouldly and xunit, none of which Core may.
        IReadOnlyList<string> violations = AssemblyReferenceRules.Check(_thisTests, AssemblyReferenceRules.Core);

        violations.ShouldContain(static v => v.StartsWith("Evenmere.Sim.Tests references Evenmere.Sim:", StringComparison.Ordinal));
        violations.ShouldContain(static v => v.StartsWith("Evenmere.Sim.Tests references Evenmere.Core:", StringComparison.Ordinal));
        violations.ShouldContain(static v => v.StartsWith("Evenmere.Sim.Tests references Shouldly:", StringComparison.Ordinal));
        violations.ShouldContain(static v => v.StartsWith("Evenmere.Sim.Tests references MessagePack: names starting with 'MessagePack' are banned", StringComparison.Ordinal));
        violations.ShouldNotContain(static v => v.StartsWith("Evenmere.Sim.Tests references System.Runtime:", StringComparison.Ordinal));
    }

    [Fact]
    public void Check_EvenmereAssemblyOutsideTheAllowList_FailsEvenWhenOtherPackagesAreAllowed()
    {
        // The Content policy allows Core only: Sim and Content itself are other layers, and packages are fine.
        IReadOnlyList<string> violations = AssemblyReferenceRules.Check(_thisTests, AssemblyReferenceRules.Content);

        violations.ShouldContain(static v => v.StartsWith("Evenmere.Sim.Tests references Evenmere.Sim: this layer may not reference that Evenmere assembly", StringComparison.Ordinal));
        violations.ShouldContain(static v => v.StartsWith("Evenmere.Sim.Tests references Evenmere.Content: this layer may not reference that Evenmere assembly", StringComparison.Ordinal));
        violations.ShouldNotContain(static v => v.Contains("references Evenmere.Core:", StringComparison.Ordinal));
        violations.ShouldNotContain(static v => v.Contains("references Shouldly:", StringComparison.Ordinal));
    }

    [Fact]
    public void Check_BannedPrefix_FailsEvenWhenEverythingElseIsAllowed()
    {
        var policy = new AssemblyReferencePolicy
        {
            Framework = FrameworkAssemblies.Names,
            BannedPrefixes = ["Shouldly"],
            AllowAnyEvenmere = true,
            AllowOtherPackages = true,
        };

        AssemblyReferenceRules.Check(_thisTests, policy).ShouldHaveSingleItem()
            .ShouldBe("Evenmere.Sim.Tests references Shouldly: names starting with 'Shouldly' are banned in this layer");
    }

    [Fact]
    public void Check_EmptyFrameworkSet_FailsBecauseNothingIsAllowedByDefault()
    {
        var policy = new AssemblyReferencePolicy { Framework = new HashSet<string>(), AllowOtherPackages = false };

        AssemblyReferenceRules.Check(_core, policy).ShouldContain(static v => v.StartsWith("Evenmere.Core references System.Runtime:", StringComparison.Ordinal));
    }

    [Fact]
    public void Check_AllowListedPackage_Passes()
    {
        // System.IO.Hashing is not part of the framework; only the allow-list lets Core reference it.
        var withoutHashing = new AssemblyReferencePolicy { Framework = FrameworkAssemblies.Names };
        var withHashing = new AssemblyReferencePolicy { Framework = FrameworkAssemblies.Names, Allowed = new HashSet<string> { "System.IO.Hashing" } };

        AssemblyReferenceRules.Check(_core, withoutHashing).ShouldHaveSingleItem().ShouldContain("Evenmere.Core references System.IO.Hashing:");
        AssemblyReferenceRules.Check(_core, withHashing).ShouldBeEmpty();
    }
}
