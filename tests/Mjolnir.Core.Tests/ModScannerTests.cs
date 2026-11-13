using System;
using System.IO;
using System.Linq;
using Mjolnir.Core.Mods;
using Xunit;

namespace Mjolnir.Core.Tests;

/// <summary>The scan of Mods\: a file that cannot be read skips that file only.</summary>
public sealed class ModScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mjolnir-scan-tests", Guid.NewGuid().ToString("N"));
    private readonly LoaderPaths _paths;

    public ModScannerTests()
    {
        _paths = new LoaderPaths(_root, Path.Combine(_root, "Mjolnir"));
        Directory.CreateDirectory(_paths.CoreDir);
        Directory.CreateDirectory(_paths.InteropDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { } // the loaded assembly's file stays open in its load context
        catch (UnauthorizedAccessException) { }
    }

    private string ModFile(string folder, string name)
    {
        string dir = Path.Combine(_paths.ModsDir, folder);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name);
    }

    [Fact]
    public void ALockedOrNativeFileSkipsOnlyItself()
    {
        string here = Path.GetDirectoryName(typeof(ModScannerTests).Assembly.Location)!;
        File.Copy(Path.Combine(here, "Il2CppInterop.Common.dll"), ModFile("Good", "Il2CppInterop.Common.dll"));
        File.WriteAllText(ModFile("Native", "native.dll"), "not a PE file");
        string locked = ModFile("Locked", "Locked.dll");
        File.Copy(Path.Combine(here, "Il2CppInterop.Common.dll"), locked);

        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var loaded = ModScanner.LoadAll(_paths);
            Assert.Equal(["Il2CppInterop.Common.dll"], loaded.Select(l => Path.GetFileName(l.Path)));
        }
    }

    [Fact]
    public void AnEmptyInstallMakesTheModsFolder()
    {
        Assert.Empty(ModScanner.LoadAll(_paths));
        Assert.True(Directory.Exists(_paths.ModsDir));
    }
}
