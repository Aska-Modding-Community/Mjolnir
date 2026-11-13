using System.Linq;
using Xunit;

namespace Mjolnir.SDK.Net.Tests;

/// <summary>Which mods keep a client out: only those every player needs, by id and version, and Mjolnir's own version.</summary>
public class CompatibilityTests
{
    internal static ModStamp Shared(string id, string version = "1.0.0") => new(id, Name(id), version, NetworkCompatibility.Everyone);
    internal static ModStamp Own(string id, NetworkCompatibility mode) => new(id, Name(id), "1.0.0", mode);
    private static string Name(string id) => char.ToUpperInvariant(id[0]) + id[1..] + " Mod";

    [Fact]
    public void TheSameModsAreCompatible()
    {
        var mods = new[] { Shared("items"), Shared("stats", "2.1") };
        var report = CompatibilityReport.Compare("0.3.0", mods, "0.3.0", mods);
        Assert.True(report.IsCompatible);
        Assert.Equal("compatible", report.Summary);
    }

    [Fact]
    public void AModTheHostNeedsMissingOnTheClientIsListed()
    {
        var report = CompatibilityReport.Compare("0.3.0", [Shared("items"), Shared("stats")], "0.3.0", [Shared("stats")]);
        Assert.False(report.IsCompatible);
        Assert.Equal(["items"], report.MissingOnClient.Select(m => m.Id));
        Assert.Empty(report.MissingOnHost);
    }

    [Fact]
    public void AModOnlyTheClientHasIsListed()
    {
        var report = CompatibilityReport.Compare("0.3.0", [], "0.3.0", [Shared("cheats")]);
        Assert.Equal(["cheats"], report.MissingOnHost.Select(m => m.Id));
    }

    [Fact]
    public void DifferentVersionsAreListedWithBoth()
    {
        var report = CompatibilityReport.Compare("0.3.0", [Shared("items", "1.2.0")], "0.3.0", [Shared("items", "1.1.0")]);
        var mismatch = Assert.Single(report.VersionMismatches);
        Assert.Equal(("Items Mod", "1.2.0", "1.1.0"), (mismatch.Name, mismatch.HostVersion, mismatch.ClientVersion));
    }

    [Theory]
    [InlineData(NetworkCompatibility.ClientOnly)]
    [InlineData(NetworkCompatibility.ServerOnly)]
    public void AModOfOnePlayersOwnKeepsNoOneOut(NetworkCompatibility mode)
    {
        Assert.True(CompatibilityReport.Compare("0.3.0", [Own("minimap", mode)], "0.3.0", []).IsCompatible);
        Assert.True(CompatibilityReport.Compare("0.3.0", [], "0.3.0", [Own("minimap", mode)]).IsCompatible);
    }

    [Fact]
    public void IdsCompareWithoutCase() =>
        Assert.True(CompatibilityReport.Compare("0.3.0", [Shared("Author.Mod")], "0.3.0", [Shared("author.mod")]).IsCompatible);

    [Theory]
    [InlineData("1.0", "1.0.0", true)]
    [InlineData("1.0.0", "1.0.0.0", true)]
    [InlineData("1.0.0", "1.0.1", false)]
    [InlineData("1.0.0-beta", "1.0.0", false)]
    [InlineData("1.0.0-Beta", "1.0.0-beta", true)]
    [InlineData("nightly", "NIGHTLY", true)]
    public void VersionsCompareByTheirParts(string a, string b, bool same) => Assert.Equal(same, CompatibilityReport.SameVersion(a, b));

    [Theory]
    [InlineData("0.3.0", "0.3.4", true)]
    [InlineData("0.3.0", "0.4.0", false)]
    [InlineData("1.3.0", "0.3.0", false)]
    public void MjolnirVersionsAgreeInMajorAndMinor(string host, string client, bool compatible)
    {
        var report = CompatibilityReport.Compare(host, [], client, []);
        Assert.Equal(compatible, report.IsCompatible);
        Assert.Equal(!compatible, report.PlatformMismatch);
    }

    [Fact]
    public void AHostWithoutMjolnirListsTheClientsSharedMods()
    {
        var report = CompatibilityReport.HostHasNoMjolnir("0.3.0", [Shared("items"), Own("minimap", NetworkCompatibility.ClientOnly)]);
        Assert.False(report.IsCompatible);
        Assert.True(report.HostWithoutMjolnir);
        Assert.Equal(["items"], report.MissingOnHost.Select(m => m.Id));
    }

    [Fact]
    public void AListNamingAnIdTwiceTakesTheFirst()
    {
        ModStamp[] client = [Shared("items", "1.0.0"), Shared("ITEMS", "9.0.0"), Shared("stats")];
        var report = CompatibilityReport.Compare("0.3.0", [Shared("items"), Shared("stats")], "0.3.0", client);
        Assert.True(report.IsCompatible);

        var other = CompatibilityReport.Compare("0.3.0", [Shared("items", "9.0.0"), Shared("items")], "0.3.0", [Shared("items")]);
        Assert.Equal("9.0.0", Assert.Single(other.VersionMismatches).HostVersion);
    }

    [Fact]
    public void NoAnswerIsNeverCompatible() => Assert.False(CompatibilityReport.NoAnswer("0.3.0", "0.3.0").IsCompatible);
}
