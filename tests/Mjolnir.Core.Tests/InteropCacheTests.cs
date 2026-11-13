using System;
using System.IO;
using Mjolnir.Core.Interop;
using Xunit;
using static Mjolnir.Core.Interop.InteropCache;

namespace Mjolnir.Core.Tests;

/// <summary>
/// Mjolnir/interop kept for the game's build: when the generator runs, what its exit codes do, and the swap that puts a
/// complete output in place only. The generator itself is a fake that writes files.
/// </summary>
public sealed class InteropCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mjolnir-interop-tests", Guid.NewGuid().ToString("N"));
    private readonly Layout _layout;
    private static readonly Stamp Build1 = new("game-1", "1.5.3.0", "Cpp2IL 1, Il2CppInterop.Generator 1.5.3");
    private int _runs;

    public InteropCacheTests()
    {
        _layout = new Layout(Path.Combine(_root, "ASKA"), Path.Combine(_root, "interop"), Path.Combine(_root, "interopgen"),
                             Path.Combine(_root, "dotnet"), Path.Combine(_root, "cache"));
        Directory.CreateDirectory(_layout.GeneratorDir);
        File.WriteAllText(_layout.GeneratorExe, "");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    // A generator that writes a set of assemblies named after the run, then exits with the code given.
    private Func<Layout, int> Generator(int code = 0, bool writes = true) => layout =>
    {
        _runs++;
        Assert.False(Directory.Exists(layout.NewDir)); // cleared before each run
        if (writes)
        {
            Directory.CreateDirectory(layout.NewDir);
            File.WriteAllText(Path.Combine(layout.NewDir, "Il2Cppmscorlib.dll"), $"run {_runs}");
            File.WriteAllText(Path.Combine(layout.NewDir, "Assembly-CSharp.dll"), $"run {_runs}");
        }
        return code;
    };

    private void Installed(Stamp? stamp, string text = "old")
    {
        Directory.CreateDirectory(_layout.InteropDir);
        File.WriteAllText(Path.Combine(_layout.InteropDir, "Il2Cppmscorlib.dll"), text);
        File.WriteAllText(Path.Combine(_layout.InteropDir, "Stale.dll"), text);
        if (stamp != null) WriteStamp(_layout.StampPath, stamp);
    }

    private string Mscorlib => File.ReadAllText(Path.Combine(_layout.InteropDir, "Il2Cppmscorlib.dll"));

    [Fact]
    public void TheFirstStartGeneratesAndStampsThem()
    {
        Assert.Equal(Outcome.Generated, Ensure(_layout, Build1, Generator()));
        Assert.Equal("run 1", Mscorlib);
        Assert.Equal(Build1, ReadStamp(_layout.StampPath));
        Assert.False(Directory.Exists(_layout.NewDir));
        Assert.False(Directory.Exists(_layout.OldDir));
    }

    [Fact]
    public void AssembliesForThisBuildAreUsedWithoutAGeneration()
    {
        Installed(Build1);
        Assert.Equal(Outcome.Current, Ensure(_layout, Build1, Generator()));
        Assert.Equal(0, _runs);
    }

    [Theory]
    [InlineData("game-2", "1.5.3.0", "Cpp2IL 1, Il2CppInterop.Generator 1.5.3")] // the game was updated
    [InlineData("game-1", "1.6.0.0", "Cpp2IL 1, Il2CppInterop.Generator 1.5.3")] // Mjolnir brought another Il2CppInterop
    [InlineData("game-1", "1.5.3.0", "Cpp2IL 2, Il2CppInterop.Generator 1.5.3")] // or another generator
    public void AnyChangeMakesThemAgainAndDropsTheOldFiles(string game, string interop, string generator)
    {
        Installed(Build1);
        var now = new Stamp(game, interop, generator);
        Assert.Equal(Outcome.Generated, Ensure(_layout, now, Generator()));
        Assert.Equal("run 1", Mscorlib);
        Assert.False(File.Exists(Path.Combine(_layout.InteropDir, "Stale.dll")));
        Assert.Equal(now, ReadStamp(_layout.StampPath));
    }

    [Fact]
    public void AssembliesWithoutAStampAreMadeAgain()
    {
        Installed(stamp: null); // a developer's deploy of earlier days, or a stamp that could not be written
        Assert.Equal(Outcome.Generated, Ensure(_layout, Build1, Generator()));
        Assert.Equal(1, _runs);
    }

    [Fact]
    public void AnUnreadableStampCountsAsNone()
    {
        Installed(Build1);
        File.WriteAllText(_layout.StampPath, "{ not json");
        Assert.Equal(Outcome.Generated, Ensure(_layout, Build1, Generator()));
    }

    [Fact]
    public void AFailedGenerationKeepsTheOldFilesAndRunsNoMods()
    {
        Installed(Build1 with { Game = "game-0" });
        Assert.Equal(Outcome.Failed, Ensure(_layout, Build1, Generator(code: 1)));
        Assert.Equal("old", Mscorlib); // never half replaced
        Assert.False(Directory.Exists(_layout.NewDir)); // its partial output cleared
        Assert.Equal("game-0", ReadStamp(_layout.StampPath)!.Game); // so the next start tries again
    }

    [Fact]
    public void AnExitCodeOfZeroWithoutOutputIsAFailure()
    {
        Assert.Equal(Outcome.Failed, Ensure(_layout, Build1, Generator(code: 0, writes: false)));
        Assert.False(Directory.Exists(_layout.InteropDir));
    }

    [Fact]
    public void ThePlayerStartingWithoutModsIsNoFailureAndIsAskedAgainNextTime()
    {
        Assert.Equal(Outcome.Cancelled, Ensure(_layout, Build1, Generator(code: CancelledExitCode)));
        Assert.False(Directory.Exists(_layout.NewDir));
        Assert.Equal(Outcome.Generated, Ensure(_layout, Build1, Generator()));
    }

    [Fact]
    public void AnOfflineFirstStartRunsNoModsAndTriesAgainNextTime()
    {
        // The generator stops when Unity's base libraries cannot be downloaded: nothing is stamped, so a start with a
        // connection generates them unstripped.
        Assert.Equal(Outcome.Failed, Ensure(_layout, Build1, Generator(code: UnityLibsMissingExitCode, writes: false)));
        Assert.False(Directory.Exists(_layout.InteropDir));
        Assert.Null(ReadStamp(_layout.StampPath));
        Assert.Equal(Outcome.Generated, Ensure(_layout, Build1, Generator()));
        Assert.Equal(2, _runs);
    }

    [Fact]
    public void AnOfflineStartAfterAGameUpdateKeepsTheOldFilesAndTheirStamp()
    {
        Installed(Build1 with { Game = "game-0" });
        Assert.Equal(Outcome.Failed, Ensure(_layout, Build1, Generator(code: UnityLibsMissingExitCode, writes: false)));
        Assert.Equal("old", Mscorlib);
        Assert.Equal("game-0", ReadStamp(_layout.StampPath)!.Game);
    }

    [Fact]
    public void ALeftoverOutputOfAnInterruptedRunIsClearedFirst()
    {
        Directory.CreateDirectory(_layout.NewDir);
        File.WriteAllText(Path.Combine(_layout.NewDir, "Half.dll"), "");
        Assert.Equal(Outcome.Generated, Ensure(_layout, Build1, Generator()));
        Assert.False(File.Exists(Path.Combine(_layout.InteropDir, "Half.dll")));
    }

    [Fact]
    public void ALeftoverOldFolderDoesNotStopTheSwap()
    {
        Installed(stamp: null);
        Directory.CreateDirectory(_layout.OldDir);
        File.WriteAllText(Path.Combine(_layout.OldDir, "x.dll"), "");
        Assert.Equal(Outcome.Generated, Ensure(_layout, Build1, Generator()));
        Assert.False(Directory.Exists(_layout.OldDir));
    }

    [Fact]
    public void WithoutAGeneratorWhatIsThereIsUsedAsItIs()
    {
        File.Delete(_layout.GeneratorExe);
        Assert.Equal(Outcome.Missing, Ensure(_layout, Build1, Generator()));
        Installed(stamp: null);
        Assert.Equal(Outcome.Unchecked, Ensure(_layout, Build1, Generator()));
        Assert.Equal(0, _runs);
    }

    [Fact]
    public void WithoutTheGamesIdentityNothingIsGenerated()
    {
        var unknown = Build1 with { Game = null };
        Assert.Equal(Outcome.Missing, Ensure(_layout, unknown, Generator()));
        Installed(Build1);
        Assert.Equal(Outcome.Unchecked, Ensure(_layout, unknown, Generator()));
        Assert.Equal(0, _runs);
    }

    [Fact]
    public void TheGeneratorWritesBesideTheFolderWithItsWindowAndLibraryCache()
    {
        Assert.Equal(["--game", _layout.GameDir, "--out", _layout.InteropDir + ".new", "--unity-libs-cache",
                      Path.Combine(_layout.CacheDir, "unity-libs"), "--window"], Arguments(_layout));
    }

    [Fact]
    public void TheGeneratorsVersionComesFromItsTwoLibraries()
    {
        Assert.Null(GeneratorVersion(_layout.GeneratorDir)); // neither there
        string here = Path.GetDirectoryName(typeof(InteropCacheTests).Assembly.Location)!;
        // Il2CppInterop.Runtime stands in for both: any managed assembly with a version.
        File.Copy(Path.Combine(here, "Il2CppInterop.Runtime.dll"), Path.Combine(_layout.GeneratorDir, "Cpp2IL.Core.dll"));
        Assert.Null(GeneratorVersion(_layout.GeneratorDir));
        File.Copy(Path.Combine(here, "Il2CppInterop.Runtime.dll"), Path.Combine(_layout.GeneratorDir, "Il2CppInterop.Generator.dll"));
        Assert.Matches(@"^Cpp2IL \d+\.\d+.*, Il2CppInterop\.Generator \d+\.\d+", GeneratorVersion(_layout.GeneratorDir));
    }

    [Fact]
    public void AVersionIsTheInformationalOneSoPreReleasesDiffer()
    {
        string here = Path.GetDirectoryName(typeof(InteropCacheTests).Assembly.Location)!;
        string runtime = Path.Combine(here, "Il2CppInterop.Runtime.dll");
        string? informational = System.Diagnostics.FileVersionInfo.GetVersionInfo(runtime).ProductVersion;
        Assert.False(string.IsNullOrWhiteSpace(informational));
        Assert.Equal(informational!.Trim(), VersionOf(runtime));
        Assert.Null(VersionOf(Path.Combine(here, "no-such.dll")));
    }

    [Fact]
    public void TheExpectedStampNamesTheRuntimesIl2CppInterop()
    {
        var expected = Expected(_layout, game: null);
        Assert.Null(expected.Game);
        Assert.Matches(@"^\d+\.\d+\.\d+", expected.Il2CppInterop);
    }
}
