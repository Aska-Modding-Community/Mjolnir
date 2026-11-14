using System;
using System.Collections.Generic;
using System.Linq;
using Mjolnir.Core.Mods;
using Xunit;

namespace Mjolnir.Core.Tests;

public class DependencyResolverTests
{
    private static ModDescriptor Mod(string id, string version = "1.0.0", params ModDependency[] deps)
    {
        Assert.True(ModVersion.TryParse(version, out var v));
        return new ModDescriptor(id, id, version, v, deps, typeof(DependencyResolverTests).Assembly, id + ".dll");
    }

    private static ModDescriptor NeedsPlatform(string id, string range) =>
        new(id, id, "1.0.0", new Version(1, 0, 0, 0), [], typeof(DependencyResolverTests).Assembly, id + ".dll",
            VersionRange.Parse(range));

    private static ModDependency Needs(string id, string range = "", bool optional = false) =>
        new(id, VersionRange.Parse(range), optional);

    private static string[] Order(ResolveResult r) => r.Ordered.Select(m => m.Id).ToArray();
    private static string[] Skipped(ResolveResult r) => r.Skipped.Select(s => s.Mod.Id).OrderBy(x => x).ToArray();
    private static string Reason(ResolveResult r, string id) => r.Skipped.Single(s => s.Mod.Id == id).Reason.ToString();
    private static ModSkip Skip(ResolveResult r, string id) => r.Skipped.Single(s => s.Mod.Id == id).Reason;
    private static HashSet<string> Off(params string[] ids) => new(ids, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void AModBuiltForAnotherMjolnirIsNotLoaded()
    {
        var r = DependencyResolver.Resolve([NeedsPlatform("a", ">=0.5")], null, new Version(0, 4, 0, 0));
        Assert.Empty(r.Ordered);
        Assert.Equal(ModSkipKind.WrongPlatform, Skip(r, "a").Kind);
        Assert.Equal("needs Mjolnir >=0.5, but 0.4.0 is installed: rebuild the mod against it", Reason(r, "a"));
    }

    [Fact]
    public void AModLoadsWhenTheInstalledMjolnirIsInItsRange()
    {
        var r = DependencyResolver.Resolve([NeedsPlatform("a", ">=0.4 <0.5")], null, new Version(0, 4, 0, 0));
        Assert.Equal(["a"], Order(r));
    }

    [Fact]
    public void AModThatNamesNoPlatformLoadsOnAny()
    {
        var r = DependencyResolver.Resolve([Mod("a")], null, new Version(9, 9, 9, 0));
        Assert.Equal(["a"], Order(r));
    }

    [Fact]
    public void WhatDependsOnAModBuiltForAnotherMjolnirIsSkippedToo()
    {
        var r = DependencyResolver.Resolve([NeedsPlatform("a", ">=0.5"), Mod("b", "1", Needs("a"))], null, new Version(0, 4, 0, 0));
        Assert.Empty(r.Ordered);
        Assert.Equal(ModSkipKind.DependencyNotLoaded, Skip(r, "b").Kind);
    }

    [Fact]
    public void LoadsDependenciesFirstAndBreaksTiesById()
    {
        var r = DependencyResolver.Resolve([Mod("c", "1", Needs("a")), Mod("b"), Mod("a"), Mod("d", "1", Needs("c"), Needs("b"))]);
        Assert.Equal(["a", "b", "c", "d"], Order(r));
        Assert.Empty(r.Skipped);
    }

    [Fact]
    public void DependencyBeatsAlphabeticalOrder()
    {
        var r = DependencyResolver.Resolve([Mod("a.faultmod", "1", Needs("z.smokemod")), Mod("z.smokemod")]);
        Assert.Equal(["z.smokemod", "a.faultmod"], Order(r));
    }

    [Fact]
    public void MissingDependencySkipsTheModAndItsDependents()
    {
        var r = DependencyResolver.Resolve([Mod("a", "1", Needs("missing", ">=1.0")), Mod("b", "1", Needs("a")), Mod("c")]);
        Assert.Equal(["c"], Order(r));
        Assert.Equal(["a", "b"], Skipped(r));
        Assert.Contains("not installed", Reason(r, "a"));
        Assert.Contains("a, which is not loaded", Reason(r, "b"));
    }

    [Fact]
    public void VersionOutsideRangeSkipsTheMod()
    {
        var r = DependencyResolver.Resolve([Mod("lib", "1.4.0"), Mod("a", "1", Needs("lib", ">=2.0"))]);
        Assert.Equal(["lib"], Order(r));
        Assert.Contains("but 1.4.0 is installed", Reason(r, "a"));
    }

    [Fact]
    public void OptionalDependencyMayBeMissingButMustMatchWhenPresent()
    {
        var missing = DependencyResolver.Resolve([Mod("a", "1", Needs("extra", optional: true))]);
        Assert.Equal(["a"], Order(missing));

        var present = DependencyResolver.Resolve([Mod("a", "1", Needs("z", ">=1.0", optional: true)), Mod("z", "1.5")]);
        Assert.Equal(["z", "a"], Order(present));

        var tooOld = DependencyResolver.Resolve([Mod("a", "1", Needs("z", ">=2.0", optional: true)), Mod("z", "1.5")]);
        Assert.Equal(["z"], Order(tooOld));
    }

    [Fact]
    public void OptionalDependencyThatIsSkippedCountsAsMissing()
    {
        var r = DependencyResolver.Resolve([Mod("a", "1", Needs("b", optional: true)), Mod("b", "1", Needs("missing"))]);
        Assert.Equal(["a"], Order(r));
        Assert.Equal(["b"], Skipped(r));
    }

    [Fact]
    public void CycleSkipsItsMembersAndDependentsOnly()
    {
        var r = DependencyResolver.Resolve([Mod("x", "1", Needs("y")), Mod("y", "1", Needs("x")), Mod("z", "1", Needs("x")), Mod("free")]);
        Assert.Equal(["free"], Order(r));
        Assert.Equal(["x", "y", "z"], Skipped(r));
        Assert.Contains("dependency cycle", Reason(r, "x"));
    }

    [Fact]
    public void DuplicateIdsKeepTheFirstIgnoringCase()
    {
        var r = DependencyResolver.Resolve([Mod("Same"), Mod("same", "2.0")]);
        Assert.Single(r.Ordered);
        Assert.Equal("1.0.0", r.Ordered[0].VersionText);
        Assert.Contains("duplicate mod id", r.Skipped.Single().Reason.ToString());
        Assert.Equal(new ModSkip(ModSkipKind.Duplicate, "same", Detail: "Same.dll"), r.Skipped.Single().Reason);
    }

    [Fact]
    public void DependencyIdsAreCaseInsensitive()
    {
        var r = DependencyResolver.Resolve([Mod("a", "1", Needs("LIB")), Mod("lib")]);
        Assert.Equal(["lib", "a"], Order(r));
    }

    [Fact]
    public void SelfDependencyIsRejected()
    {
        var r = DependencyResolver.Resolve([Mod("a", "1", Needs("a"))]);
        Assert.Empty(r.Ordered);
        Assert.Equal("depends on itself", Reason(r, "a"));
    }

    [Fact]
    public void ReasonsNameWhatTheyAreAbout()
    {
        var r = DependencyResolver.Resolve([Mod("lib", "1.4.0"), Mod("a", "1", Needs("lib", ">=2.0")), Mod("b", "1", Needs("gone", ">=1.0")),
                                            Mod("c", "1", Needs("b"))]);
        Assert.Equal(new ModSkip(ModSkipKind.WrongVersion, "lib", ">=2.0", "1.4.0"), Skip(r, "a"));
        Assert.Equal(new ModSkip(ModSkipKind.Missing, "gone", ">=1.0"), Skip(r, "b"));
        Assert.Equal(new ModSkip(ModSkipKind.DependencyNotLoaded, "b"), Skip(r, "c"));
        // The log keeps its English texts.
        Assert.Equal("needs lib >=2.0, but 1.4.0 is installed", Reason(r, "a"));
        Assert.Equal("needs gone >=1.0, which is not installed", Reason(r, "b"));
        Assert.Equal("needs b, which is not loaded", Reason(r, "c"));
    }

    [Fact]
    public void ATurnedOffModIsSkippedAndSoAreModsThatNeedIt()
    {
        var r = DependencyResolver.Resolve([Mod("lib"), Mod("a", "1", Needs("lib")), Mod("b", "1", Needs("a")), Mod("free")], Off("LIB"));
        Assert.Equal(["free"], Order(r));
        Assert.Equal(ModSkipKind.TurnedOff, Skip(r, "lib").Kind);
        Assert.Equal(new ModSkip(ModSkipKind.DependencyTurnedOff, "lib"), Skip(r, "a"));
        Assert.Equal(new ModSkip(ModSkipKind.DependencyNotLoaded, "a"), Skip(r, "b"));
        Assert.Equal("turned off in the Mods window", Reason(r, "lib"));
        Assert.Equal("needs lib, which is turned off", Reason(r, "a"));
    }

    [Fact]
    public void AnOptionalDependencyThatIsTurnedOffDoesNotBlock()
    {
        var r = DependencyResolver.Resolve([Mod("a", "1", Needs("extra", optional: true)), Mod("extra")], Off("extra"));
        Assert.Equal(["a"], Order(r));
        Assert.Equal(["extra"], Skipped(r));
    }

    [Fact]
    public void ATurnedOffModIsReportedAsTurnedOffEvenIfItCouldNotLoad()
    {
        var r = DependencyResolver.Resolve([Mod("a", "1", Needs("missing"))], Off("a", "not.installed"));
        Assert.Equal(ModSkipKind.TurnedOff, Skip(r, "a").Kind);
    }
}
