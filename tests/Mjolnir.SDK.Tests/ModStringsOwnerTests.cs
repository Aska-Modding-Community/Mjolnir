using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using Xunit;

namespace Mjolnir.SDK.Tests;

/// <summary>
/// Which mod's strings an assembly asks for (<see cref="ModStringsRegistry.ForAssembly"/>): a library in a mod's load
/// context belongs to the mod beside it, even while that mod is still in its OnLoad (not registered yet), and a library
/// asked about before its mod could be told is found once it can, rather than taken for no mod for good.
/// </summary>
[Collection("MainThread")]
public sealed class ModStringsOwnerTests : IDisposable
{
    public ModStringsOwnerTests() => ModStringsRegistry.ResetForTests();

    public void Dispose() => ModStringsRegistry.ResetForTests();

    [Fact]
    public void AModsOwnAssemblyIsFoundByItsManifest()
    {
        var context = new AssemblyLoadContext("ModStrings.Own", isCollectible: true);
        var mod = Make(context, "ModStrings.Own.Mod", "own.mod");
        Assert.Equal("own.mod", ModStringsRegistry.ForAssembly(mod)?.ModId);
    }

    [Fact]
    public void ALibraryBelongsToTheModInItsLoadContext()
    {
        var context = new AssemblyLoadContext("ModStrings.Library", isCollectible: true);
        Make(context, "ModStrings.Library.Mod", "library.mod");
        var library = Make(context, "ModStrings.Library.Helpers", manifestId: null);
        Assert.Equal("library.mod", ModStringsRegistry.ForAssembly(library)?.ModId);
    }

    [Fact]
    public void ALibraryAskedAboutBeforeItsModIsFoundLater()
    {
        var context = new AssemblyLoadContext("ModStrings.Early", isCollectible: true);
        var library = Make(context, "ModStrings.Early.Helpers", manifestId: null);
        Assert.Null(ModStringsRegistry.ForAssembly(library));

        Make(context, "ModStrings.Early.Mod", "early.mod");
        Assert.Equal("early.mod", ModStringsRegistry.ForAssembly(library)?.ModId);
    }

    [Fact]
    public void PlatformCodeIsNoMod() => Assert.Null(ModStringsRegistry.ForAssembly(typeof(Mod).Assembly));

    // An assembly made in memory in the given load context, with a mod class carrying a manifest when an id is given.
    private static Assembly Make(AssemblyLoadContext context, string name, string? manifestId)
    {
        using (context.EnterContextualReflection())
        {
            var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(name), AssemblyBuilderAccess.RunAndCollect);
            var module = assembly.DefineDynamicModule(name);
            if (manifestId == null)
            {
                module.DefineType("Helpers", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed).CreateType();
                return assembly;
            }
            var type = module.DefineType("TheMod", TypeAttributes.Public | TypeAttributes.Abstract, typeof(Mod));
            var manifest = typeof(ModManifestAttribute).GetConstructor([typeof(string), typeof(string), typeof(string)])!;
            type.SetCustomAttribute(new CustomAttributeBuilder(manifest, [manifestId, name, "1.0.0"]));
            type.CreateType();
            return assembly;
        }
    }
}
