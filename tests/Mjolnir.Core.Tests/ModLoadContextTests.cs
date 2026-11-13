using System;
using System.IO;
using Mjolnir.Core.Mods;
using Xunit;

namespace Mjolnir.Core.Tests;

public sealed class ModLoadContextTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mjolnir-alc-tests", Guid.NewGuid().ToString("N"));
    private readonly LoaderPaths _paths;

    public ModLoadContextTests()
    {
        _paths = new LoaderPaths(_root, Path.Combine(_root, "Mjolnir"));
        Directory.CreateDirectory(_paths.CoreDir);
        Directory.CreateDirectory(_paths.InteropDir);
        File.WriteAllText(Path.Combine(_paths.CoreDir, "Mjolnir.SDK.dll"), "");
        File.WriteAllText(Path.Combine(_paths.CoreDir, "Mjolnir.SDK.UI.dll"), "");
        File.WriteAllText(Path.Combine(_paths.CoreDir, "Mjolnir.SDK.Player.dll"), "");
        File.WriteAllText(Path.Combine(_paths.CoreDir, "Mjolnir.SDK.Content.dll"), "");
        File.WriteAllText(Path.Combine(_paths.CoreDir, "Tomlyn.dll"), "");
        File.WriteAllText(Path.Combine(_paths.InteropDir, "Assembly-CSharp.dll"), "");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("Mjolnir.SDK")]
    [InlineData("Mjolnir.SDK.UI")] // an SDK module: one copy for every mod, never one in a mod's folder
    [InlineData("Mjolnir.SDK.Player")]
    [InlineData("Mjolnir.SDK.Content")]
    [InlineData("Tomlyn")]
    [InlineData("Assembly-CSharp")]
    public void AssembliesShippedInCoreOrInteropAreShared(string name) => Assert.True(ModLoadContext.IsShared(name, _paths));

    [Theory]
    [InlineData("Mjolnir.SmokeMod")]   // regression: a mod named Mjolnir.* is not platform code
    [InlineData("SomeLibrary")]
    [InlineData(null)]
    public void OtherAssembliesAreNotShared(string? name) => Assert.False(ModLoadContext.IsShared(name, _paths));
}
