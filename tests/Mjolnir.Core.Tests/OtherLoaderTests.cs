using System;
using System.IO;
using Xunit;

namespace Mjolnir.Core.Tests;

/// <summary>
/// Another mod loader installed in the game's folder beside Mjolnir: it never starts and never says so, and this is
/// what finds it for the log and for the Mods window.
/// </summary>
public sealed class OtherLoaderTests : IDisposable
{
    private readonly string _game = Path.Combine(Path.GetTempPath(), "mjolnir-otherloader-tests", Guid.NewGuid().ToString("N"));

    public OtherLoaderTests() => Directory.CreateDirectory(_game);

    public void Dispose()
    {
        if (Directory.Exists(_game)) Directory.Delete(_game, recursive: true);
    }

    private void Touch(string name) => File.WriteAllText(Path.Combine(_game, name), "");
    private void Folder(string name) => Directory.CreateDirectory(Path.Combine(_game, name));

    [Fact]
    public void AGameWithOnlyMjolnirHasNoOtherLoader()
    {
        Touch("version.dll");
        Folder("Mjolnir");
        Assert.Null(OtherLoader.Find(_game));
    }

    [Theory]
    [InlineData("doorstop_config.ini")]
    [InlineData(".doorstop_version")]
    public void EitherOfDoorstopsFilesIsEnough(string marker)
    {
        Touch(marker);
        var found = OtherLoader.Find(_game);
        Assert.Equal("Unity Doorstop", found?.Name); // without BepInEx's folder, only the doorstop itself is known
        Assert.Equal(marker, found?.Marker);
    }

    [Fact]
    public void DoorstopWithBepInExFolderIsNamedBepInEx()
    {
        Touch("doorstop_config.ini");
        Touch("winhttp.dll");
        Folder("BepInEx");
        Assert.Equal("BepInEx", OtherLoader.Find(_game)?.Name);
    }

    [Fact]
    public void MelonLoadersFolderIsFoundToo()
    {
        // Its own proxy is version.dll, the file Mjolnir ships: the folder is here, the loader is not.
        Folder("MelonLoader");
        var found = OtherLoader.Find(_game);
        Assert.Equal("MelonLoader", found?.Name);
        Assert.Equal("MelonLoader", found?.Marker);
    }

    [Fact]
    public void ABepInExFolderWithoutADoorstopIsNotALoader()
    {
        Folder("BepInEx"); // left over after an uninstall: nothing hooks the game
        Assert.Null(OtherLoader.Find(_game));
    }

    [Fact]
    public void AFolderThatIsNotThereFindsNothingAndDoesNotThrow() =>
        Assert.Null(OtherLoader.Find(Path.Combine(_game, "gone")));
}
