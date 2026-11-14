using System;
using Mjolnir.Core.Mods;
using Xunit;

namespace Mjolnir.SDK.Tests;

/// <summary>
/// Who must have a mod in a multiplayer session: the manifest's own mode, and the raise to
/// <see cref="NetworkCompatibility.Everyone"/> a module asks for when the mod adds content of its own.
/// </summary>
public class ModNetworkTests
{
    private static ModManifest Manifest(string id, NetworkCompatibility? chosen = null)
    {
        var attribute = new ModManifestAttribute(id, id, "1.0.0");
        if (chosen != null) attribute.Network = chosen.Value;
        var descriptor = new ModDescriptor(id, id, "1.0.0", new Version(1, 0, 0, 0), [],
                                           typeof(ModNetworkTests).Assembly, $"C:/Mods/{id}/{id}.dll");
        return new ModManifest(descriptor, attribute);
    }

    [Fact]
    public void AModThatChoseNoModeIsRaisedByItsOwnContent()
    {
        var manifest = Manifest("test.net.raised");
        Assert.Equal(NetworkCompatibility.ClientOnly, manifest.Network);
        manifest.NeedsEveryone("the item twine");
        Assert.Equal(NetworkCompatibility.Everyone, manifest.Network);
    }

    [Theory]
    [InlineData(NetworkCompatibility.ClientOnly)]
    [InlineData(NetworkCompatibility.ServerOnly)]
    public void AModKeepsTheModeItChose(NetworkCompatibility chosen)
    {
        // Only the mod's author can tell whether that is right; the log says what it means for a session.
        var manifest = Manifest("test.net.chosen", chosen);
        manifest.NeedsEveryone("the item twine");
        Assert.Equal(chosen, manifest.Network);
    }

    [Fact]
    public void AModThatAsksForEveryoneItselfIsLeftAsItIs()
    {
        var manifest = Manifest("test.net.everyone", NetworkCompatibility.Everyone);
        manifest.NeedsEveryone("the item twine");
        Assert.Equal(NetworkCompatibility.Everyone, manifest.Network);
    }

    [Fact]
    public void AModIsRaisedByItsId()
    {
        var manifest = Manifest("test.net.tracked");
        ModNetwork.Track(manifest);
        ModNetwork.NeedsEveryone("TEST.NET.TRACKED", "the building altar"); // ids are compared as the loader compares them
        Assert.Equal(NetworkCompatibility.Everyone, manifest.Network);
    }

    [Fact]
    public void ContentOfSomethingThatIsNoModIsIgnored() =>
        ModNetwork.NeedsEveryone("test.net.unknown", "the item twine"); // a test, or code that is no loaded mod
}
