using System;
using System.IO;
using System.Linq;
using Mjolnir.Core.Mods;
using Xunit;

namespace Mjolnir.SDK.Tests;

/// <summary>
/// The installed mods behind the Mods window: the player's choices for the next start, written to Core's mod list,
/// which changes wait for a restart, the mods that need one another, and Core's skip reasons in the SDK's terms.
/// </summary>
public sealed class InstalledModsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mjolnir-installed-tests", Guid.NewGuid().ToString("N"));
    private readonly string _list;

    private readonly InstalledMod _lib = InstalledMod.Make("lib", "Lib", "1.0", InstalledState.Running);
    private readonly InstalledMod _user;
    private readonly InstalledMod _off = InstalledMod.Make("off", "Off", "1.0", InstalledState.TurnedOff);
    private readonly InstalledMod _broken = InstalledMod.Make("broken", "Broken", "1.0", InstalledState.NotLoaded,
                                                              new InstalledProblem(InstalledProblemKind.Missing, "gone", ">=1.0"));

    public InstalledModsTests()
    {
        _list = Path.Combine(_root, ModList.FileName);
        _user = InstalledMod.Make("user", "User", "1.0", InstalledState.Running, dependencies:
                                  [new ModDependencyInfo("LIB", "", false), new ModDependencyInfo("off", "", false), new ModDependencyInfo("extra", "", true)]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private InstalledMods Mods(params string[] turnedOff) => new([_lib, _user, _off, _broken], _list, turnedOff);

    [Fact]
    public void AChoiceIsWrittenAtOnceAndKeepsIdsOfModsNotInstalled()
    {
        var mods = Mods("off", "not.installed");
        Assert.True(mods.IsEnabled(_lib));
        Assert.False(mods.IsEnabled(_off));

        Assert.True(mods.SetEnabled(_lib, false));
        Assert.True(mods.SetEnabled(_off, true));
        Assert.False(mods.IsEnabled(_lib));
        Assert.Equal(["lib", "not.installed"], ModList.Read(_list).OrderBy(id => id));
    }

    [Fact]
    public void OnlyChangesToWhatRunsWaitForARestart()
    {
        var mods = Mods("off");
        Assert.False(mods.HasPendingChanges);

        mods.SetEnabled(_broken, false); // it does not run anyway
        Assert.False(mods.IsPending(_broken));
        Assert.False(mods.HasPendingChanges);

        mods.SetEnabled(_lib, false);
        Assert.True(mods.IsPending(_lib));
        mods.SetEnabled(_lib, true);
        Assert.False(mods.HasPendingChanges);

        mods.SetEnabled(_off, true);
        Assert.True(mods.IsPending(_off));
        Assert.True(mods.HasPendingChanges);
    }

    [Fact]
    public void ModsThatNeedOneAnotherAreFoundIgnoringCaseAndOptionalDependencies()
    {
        var mods = Mods("off");
        Assert.Equal([_user], mods.RequiredBy(_lib));
        Assert.Equal([_off], mods.TurnedOffRequirements(_user)); // lib is on, extra is optional and absent

        mods.SetEnabled(_user, false);
        Assert.Empty(mods.RequiredBy(_lib)); // a mod that is off needs nothing
    }

    [Fact]
    public void WithoutAListFileNothingCanBeTurnedOff()
    {
        var mods = new InstalledMods([_lib], "", []);
        Assert.False(mods.SetEnabled(_lib, false));
        Assert.True(mods.IsEnabled(_lib));
    }

    [Fact]
    public void EveryReasonOfCoreHasOneInTheSdk()
    {
        foreach (var kind in Enum.GetValues<ModSkipKind>())
        {
            var problem = InstalledMods.ProblemOf(new ModSkip(kind, "other", ">=1.0", "detail"));
            if (kind == ModSkipKind.TurnedOff)
            {
                Assert.Null(problem); // the state says it
                continue;
            }
            Assert.NotNull(problem);
            Assert.Equal(kind.ToString(), problem.Kind.ToString());
            Assert.Equal(("other", ">=1.0", "detail"), (problem.Other, problem.Range, problem.Detail));
        }
        Assert.Null(InstalledMods.ProblemOf(null));
    }

    [Fact]
    public void AModMadeFromPlainValuesHasItsManifest()
    {
        var mod = InstalledMod.Make("a.mod", "A Mod", "1.2.3", InstalledState.TurnedOff, description: "Does things");
        Assert.Equal(("a.mod", "A Mod", "1.2.3", "Does things"), (mod.Id, mod.Manifest.Name, mod.Manifest.VersionText, mod.Manifest.Description));
        Assert.Null(mod.Mod);
        Assert.Equal(["lib", "off"], _user.Requires.Select(id => id.ToLowerInvariant()));
    }
}
