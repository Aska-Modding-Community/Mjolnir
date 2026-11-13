using System;
using Mjolnir.Core.Mods;
using Xunit;

namespace Mjolnir.Core.Tests;

// Discovery works through Core's contracts only: these fakes stand in for the SDK's attributes and Mod class,
// which shows that Core needs no SDK.

[AttributeUsage(AttributeTargets.Class)]
internal sealed class FakeManifestAttribute(string id, string name, string version) : Attribute, IModManifest
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Version { get; } = version;
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
internal sealed class FakeDependencyAttribute(string id, string range = "", bool optional = false) : Attribute, IModDependencyDeclaration
{
    public string Id { get; } = id;
    public string VersionRange { get; } = range;
    public bool Optional { get; } = optional;
}

/// <summary>Stands for an unrelated attribute such as [HarmonyPatch(typeof(SomeGameType))]: must never be created.</summary>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class ExplodingAttribute : Attribute
{
    public ExplodingAttribute() => throw new InvalidOperationException("discovery created an unrelated attribute");
}

public abstract class FakeModBase : IModEntry
{
    void IModEntry.Load() { }
    void IModEntry.Attach() { }
}

[FakeManifest("good.mod", "Good Mod", "1.2.3-beta")]
[FakeDependency("lib", ">=1.0")]
[FakeDependency("extra", optional: true)]
[Exploding]
public sealed class GoodMod : FakeModBase;

[FakeManifest("other.mod", "", "1.0")]
public sealed class OtherMod : FakeModBase;

public sealed class UnmarkedMod : FakeModBase;

public sealed class Helper;

[FakeManifest("Bad Id!", "x", "1.0")]
public sealed class BadIdMod : FakeModBase;

[FakeManifest("Mjolnir", "x", "1.0")]
public sealed class ReservedIdMod : FakeModBase;

[FakeManifest("bad.version", "x", "one")]
public sealed class BadVersionMod : FakeModBase;

[FakeManifest("bad.range", "x", "1.0")]
[FakeDependency("lib", ">=x")]
public sealed class BadRangeMod : FakeModBase;

[FakeManifest("ctor.mod", "x", "1.0")]
public sealed class NoDefaultCtorMod(int value) : FakeModBase
{
    public int Value { get; } = value;
}

[FakeManifest("not.entry", "x", "1.0")]
public sealed class NotAnEntry;

public class ModDiscoveryTests
{
    private static ModCandidate? Inspect(params Type[] types) =>
        ModDiscovery.Inspect(types, typeof(ModDiscoveryTests).Assembly, "C:/Mods/Test/Test.dll");

    [Fact]
    public void ReadsTheManifestAndDependenciesThroughTheContracts()
    {
        var candidate = Inspect(typeof(GoodMod), typeof(Helper), typeof(UnmarkedMod));
        Assert.NotNull(candidate);
        Assert.Equal(typeof(GoodMod), candidate!.EntryType);
        var mod = candidate.Mod;
        Assert.Equal("good.mod", mod.Id);
        Assert.Equal("Good Mod", mod.Name);
        Assert.Equal("1.2.3-beta", mod.VersionText);
        Assert.Equal(new Version(1, 2, 3, 0), mod.Version);
        Assert.Equal("C:/Mods/Test/Test.dll", mod.Location);
        Assert.Collection(mod.Dependencies,
            d => { Assert.Equal("lib", d.Id); Assert.False(d.Optional); },
            d => { Assert.Equal("extra", d.Id); Assert.True(d.Optional); });
    }

    [Fact]
    public void EmptyNameFallsBackToTheId() => Assert.Equal("other.mod", Inspect(typeof(OtherMod))!.Mod.Name);

    [Fact]
    public void AssemblyWithoutAManifestIsALibrary()
    {
        Assert.Null(Inspect(typeof(Helper)));
        Assert.Null(Inspect(typeof(UnmarkedMod)));
    }

    [Fact]
    public void MoreThanOneModClassIsRejected() => Assert.Null(Inspect(typeof(GoodMod), typeof(OtherMod)));

    [Theory]
    [InlineData(typeof(BadIdMod))]
    [InlineData(typeof(ReservedIdMod))] // the platform's settings file is mjolnir.toml
    [InlineData(typeof(BadVersionMod))]
    [InlineData(typeof(BadRangeMod))]
    [InlineData(typeof(NoDefaultCtorMod))]
    [InlineData(typeof(NotAnEntry))]
    public void InvalidModsAreRejected(Type type) => Assert.Null(Inspect(type));
}
