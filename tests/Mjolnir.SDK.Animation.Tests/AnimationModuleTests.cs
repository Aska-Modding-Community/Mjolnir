using System;
using System.Linq;
using System.Reflection;
using Mjolnir.Tests.Shared;
using Xunit;

namespace Mjolnir.SDK.Animation.Tests;

/// <summary>
/// Guards the Mjolnir.SDK.Animation module: no Core or game type in its public API, Core reached only through Mjolnir.SDK, no
/// other module needed, the event convention, and only its tests seeing its internals.
/// </summary>
public class AnimationArchitectureTests
{
    private static readonly Assembly Module = typeof(AnimationModule).Assembly;

    [Fact]
    public void TheCallingModIsFoundThroughTheHelper() => Assert.Empty(ApiSurface.RawCallingAssemblyUses(Module));

    [Fact]
    public void NoPublicSignatureUsesACoreType() => Assert.Empty(ApiSurface.Leaks(Module, ApiSurface.IsCore));

    [Fact]
    public void NoPublicSignatureUsesAGameType() => Assert.Empty(ApiSurface.Leaks(Module, ApiSurface.IsGame));

    [Fact]
    public void EventsFollowTheConvention() => Assert.Empty(ApiSurface.EventConventionBreaks(Module));

    [Fact]
    public void TheModuleReachesCoreOnlyThroughTheSdk() =>
        Assert.DoesNotContain("Mjolnir.Core", Module.GetReferencedAssemblies().Select(a => a.Name));

    [Theory]
    [InlineData("Mjolnir.SDK.UI")]
    [InlineData("Mjolnir.SDK.Player")]
    [InlineData("Mjolnir.SDK.Content")]
    [InlineData("Mjolnir.SDK.AI")]
    [InlineData("Mjolnir.SDK.World")]
    [InlineData("Mjolnir.SDK.Stats")]
    [InlineData("Mjolnir.SDK.Net")]
    public void TheModuleNeedsNoOtherModule(string module) =>
        Assert.DoesNotContain(module, Module.GetReferencedAssemblies().Select(a => a.Name));

    [Fact]
    public void OnlyItsTestsSeeItsInternals() => Assert.Equal(["Mjolnir.SDK.Animation.Tests"], ApiSurface.FriendsOf(Module));
}

/// <summary>The animation module is an SDK module: Mjolnir.SDK finds it next to itself.</summary>
public class SdkModuleTests
{
    [Fact]
    public void TheSdkFindsTheAnimationModuleNextToItself()
    {
        string folder = System.IO.Path.GetDirectoryName(typeof(AnimationModule).Assembly.Location)!;
        Assert.Contains(typeof(AnimationModule), SdkModules.Discover(folder));
    }
}

/// <summary>Mods' animations and masks: one object per name, names checked, non-mods refused, paths from the mod's folder.</summary>
public class ModAnimationsTests
{
    private static readonly Assembly ModAssembly = typeof(ModAnimationsTests).Assembly;

    private static ModAnimations Registry(string? folder = @"C:\Game\Mjolnir\Mods\My") =>
        new(assembly => assembly == ModAssembly ? ("my.mod", folder) : null);

    [Fact]
    public void AnAnimationIsOneObjectPerNameWithItsFullNameAndPath()
    {
        var registry = Registry();
        var bow = registry.AddAnimation(ModAssembly, "bow", "bundles/anims", "Bow");
        Assert.Same(bow, registry.AddAnimation(ModAssembly, "bow", "bundles/anims", "Bow"));
        Assert.Equal("my.mod.bow", bow.FullName);
        Assert.Equal("my.mod", bow.ModId);
        Assert.Equal(System.IO.Path.GetFullPath(@"C:\Game\Mjolnir\Mods\My\bundles\anims"), bow.BundlePath);
        Assert.Equal(TimeSpan.Zero, bow.Length); // known at its first play
        Assert.Same(bow, Animations.Find("my.mod.bow"));
        Assert.Throws<ArgumentException>(() => registry.AddAnimation(ModAssembly, "bow", "bundles/anims", "Bow2"));
    }

    [Fact]
    public void NamesAreLettersDigitsAndUnderscoresAndOnlyModsAddThem()
    {
        var registry = Registry();
        Assert.Throws<ArgumentException>(() => registry.AddAnimation(ModAssembly, "a bow", "b", "c"));
        Assert.Throws<ArgumentException>(() => registry.AddAnimation(ModAssembly, "bow", " ", "c"));
        Assert.Throws<InvalidOperationException>(() => registry.AddAnimation(typeof(object).Assembly, "bow", "b", "c"));
        Assert.Throws<ArgumentException>(() => registry.AddMask(ModAssembly, "", "b", "c"));
    }

    [Fact]
    public void AMaskIsFoundByItsFullNameAndMjolnirsByItsName()
    {
        var registry = Registry();
        var arms = registry.AddMask(ModAssembly, "arms", "bundles/anims", "Arms");
        Assert.Same(arms, registry.AddMask(ModAssembly, "arms", "bundles/anims", "Arms"));
        Assert.Equal("my.mod.arms", arms.ToString());
        Assert.Same(arms, registry.Mask("my.mod.arms"));
        Assert.Same(AnimationMask.UpperBody, registry.Mask("UpperBody"));
        Assert.Null(registry.Mask("other.mod.arms"));
    }
}

/// <summary>The messages between players: a play and a stop there and back, and what a side cannot read.</summary>
public class AnimationWireTests
{
    private static AnimationMask? Masks(string name) => name == "UpperBody" ? AnimationMask.UpperBody : null;

    [Fact]
    public void APlayGoesThereAndBack()
    {
        var sent = new AnimationOptions
        {
            Loop = true, Speed = 1.5, FadeIn = TimeSpan.FromSeconds(0.3), FadeOut = TimeSpan.Zero,
            Duration = TimeSpan.FromSeconds(4), Mask = AnimationMask.UpperBody, RootMotion = true,
        };
        Assert.True(AnimationWire.TryRead(AnimationWire.Play(42, "my.mod.bow", sent), Masks, out uint id, out string? name, out var got));
        Assert.Equal(42u, id);
        Assert.Equal("my.mod.bow", name);
        Assert.True(got!.Loop);
        Assert.Equal(1.5, got.Speed);
        Assert.Equal(TimeSpan.FromSeconds(0.3), got.FadeIn);
        Assert.Equal(TimeSpan.Zero, got.FadeOut);
        Assert.Equal(TimeSpan.FromSeconds(4), got.Duration);
        Assert.Same(AnimationMask.UpperBody, got.Mask);
        Assert.False(got.RootMotion); // the network moves the character there

        Assert.True(AnimationWire.TryRead(AnimationWire.Play(7, "aska_wave", AnimationOptions.Default), Masks, out _, out _, out var plain));
        Assert.Null(plain!.Duration);
        Assert.Null(plain.Mask);
    }

    [Fact]
    public void AStopCarriesOnlyTheCharacter()
    {
        Assert.True(AnimationWire.TryRead(AnimationWire.Stop(9), Masks, out uint id, out string? name, out var options));
        Assert.Equal(9u, id);
        Assert.Null(name);
        Assert.Null(options);
    }

    [Fact]
    public void WhatASideCannotReadIsRefused()
    {
        byte[] play = AnimationWire.Play(1, "x", new AnimationOptions { Mask = new AnimationMask("arms", "other.mod", "b", "Arms") });
        Assert.False(AnimationWire.TryRead(play, Masks, out _, out _, out _)); // a mask this side does not have
        Assert.False(AnimationWire.TryRead(play[..8], Masks, out _, out _, out _));
        Assert.False(AnimationWire.TryRead([2, 1, 0, 0, 0, 0], Masks, out _, out _, out _));
        Assert.False(AnimationWire.TryRead([], Masks, out _, out _, out _));
    }

    [Fact]
    public void ABrokenMessageIsRefusedWithoutThrowing()
    {
        byte[] play = AnimationWire.Play(1, "aska_wave", AnimationOptions.Default);
        byte[] badLength = [.. play[..6], 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, .. play[6..]]; // no 7-bit length ends so
        Assert.False(AnimationWire.TryRead(badLength, Masks, out _, out _, out _));

        byte[] notUtf8 = (byte[])play.Clone();
        notUtf8[7] = 0xFF; // the name's first character
        Assert.False(AnimationWire.TryRead(notUtf8, Masks, out _, out _, out _));

        byte[] noSpeed = (byte[])play.Clone();
        BitConverter.GetBytes(double.NaN).CopyTo(noSpeed, 6 + 1 + "aska_wave".Length + 1);
        Assert.False(AnimationWire.TryRead(noSpeed, Masks, out _, out _, out _));
    }
}
