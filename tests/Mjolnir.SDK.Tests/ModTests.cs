using System;
using System.IO;
using Mjolnir.Core.Mods;
using Xunit;

namespace Mjolnir.SDK.Tests;

[ModManifest("test.sdk.mod", "SDK Test Mod", "2.1.0", Network = NetworkCompatibility.ClientOnly,
             Description = "  Tests the SDK.  ", Author = " ")]
[ModDependency("test.lib", ">=1.0 <2.0")]
[ModDependency("test.extra", Optional = true)]
public sealed class SampleMod : Mod
{
    public string? SeenInConstructor { get; }

    public SampleMod() => SeenInConstructor = Manifest.Id;   // injected context is usable in the constructor

    public int Loads { get; private set; }

    public override void OnLoad() => Loads++;
}

public class ModTests
{
    private static ModDescriptor Descriptor() =>
        new("test.sdk.mod", "SDK Test Mod", "2.1.0", new Version(2, 1, 0, 0),
            [new ModDependency("test.lib", VersionRange.Parse(">=1.0 <2.0"), false)],
            typeof(SampleMod).Assembly, "C:/Mods/Sample/Sample.dll");

    private static SampleMod Create(string configPath) =>
        (SampleMod)ModContext.Create(typeof(SampleMod), new ModContext(Descriptor(), configPath));

    [Fact]
    public void ModCannotBeCreatedOutsideTheLoader()
    {
        var e = Assert.Throws<InvalidOperationException>(() => new SampleMod());
        Assert.Contains("mod loader", e.Message);
    }

    [Fact]
    public void LoaderContextIsInjectedIntoTheConstructor()
    {
        var mod = Create(Path.Combine(Path.GetTempPath(), "unused.toml"));
        Assert.Equal("test.sdk.mod", mod.SeenInConstructor);
        Assert.Equal("SDK Test Mod", mod.Manifest.Name);
        Assert.Equal("2.1.0", mod.Manifest.VersionText);
        Assert.Equal(NetworkCompatibility.ClientOnly, mod.Manifest.Network);
        Assert.Equal("Tests the SDK.", mod.Manifest.Description); // trimmed
        Assert.Null(mod.Manifest.Author);                          // blank counts as not given
        Assert.Equal(new ModDependencyInfo("test.lib", ">=1.0 <2.0", false), Assert.Single(mod.Manifest.Dependencies));
        Assert.Null(ModContext.Creating);   // cleared after construction
    }

    [Fact]
    public void HarmonyIsLazyAndUsesTheModId()
    {
        var mod = Create(Path.Combine(Path.GetTempPath(), "unused.toml"));
        Assert.Same(mod.Harmony, mod.Harmony);
        Assert.Equal("test.sdk.mod", mod.Harmony.Id);
    }

    [Fact]
    public void LoadRunsOnLoadSavesTheConfigAndRegistersTheMod()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mjolnir-sdk-tests", Guid.NewGuid().ToString("N"));
        try
        {
            string configPath = Path.Combine(dir, "test.sdk.mod.toml");
            var mod = Create(configPath);
            mod.Config.Bind("General", "Enabled", true);
            ((IModEntry)mod).Load();
            Assert.Equal(1, mod.Loads);
            Assert.True(File.Exists(configPath));
            Assert.True(ModRegistry.IsLoaded("TEST.SDK.MOD"));
            Assert.IsNotType<ModManifest[]>(ModRegistry.Loaded); // the registry's own list cannot be written through it
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TheIconIsTheNamedFileElseIconPngNextToTheDll()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mjolnir-sdk-tests", Guid.NewGuid().ToString("N"));
        string dll = Path.Combine(dir, "My.Mod.dll");
        var warnings = new System.Collections.Generic.List<string>();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "art"));
            Assert.Null(ModManifest.FindIcon(dll, null, warnings.Add)); // nothing there: the window shows a letter

            File.WriteAllBytes(Path.Combine(dir, "icon.png"), [1]);
            Assert.Equal(Path.Combine(dir, "icon.png"), ModManifest.FindIcon(dll, null, warnings.Add));

            File.WriteAllBytes(Path.Combine(dir, "art", "logo.png"), [1]);
            Assert.Equal(Path.Combine(dir, "art", "logo.png"), ModManifest.FindIcon(dll, "art/logo.png", warnings.Add));
            Assert.Empty(warnings);

            // Named but missing: reported, and icon.png stands in.
            Assert.Equal(Path.Combine(dir, "icon.png"), ModManifest.FindIcon(dll, "missing.png", warnings.Add));
            Assert.Contains("missing.png", Assert.Single(warnings));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AttributesImplementCoreContracts()
    {
        IModManifest manifest = new ModManifestAttribute("a.b", "A", "1.0");
        IModDependencyDeclaration dependency = new ModDependencyAttribute("c.d") { Optional = true };
        Assert.Equal("a.b", manifest.Id);
        Assert.Equal("c.d", dependency.Id);
        Assert.Equal("*", dependency.VersionRange);
        Assert.True(dependency.Optional);
    }

    [Fact]
    public void AModIsOnlyTheInstallingPlayersUntilItAsksForEveryone()
    {
        // The default keeps a mod of one player's own out of the handshake; content declares Everyone itself.
        Assert.Equal(NetworkCompatibility.ClientOnly, new ModManifestAttribute("a.b", "A", "1.0").Network);
    }
}
