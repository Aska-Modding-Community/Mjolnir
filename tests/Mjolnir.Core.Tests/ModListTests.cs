using System;
using System.IO;
using Mjolnir.Core.Mods;
using Xunit;

namespace Mjolnir.Core.Tests;

/// <summary>Mjolnir/Config/mod-list.json, the mods the player turned off: read before loading, written by the SDK.</summary>
public sealed class ModListTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mjolnir-modlist-tests", Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public ModListTests() => _path = Path.Combine(_root, "Config", ModList.FileName);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void WithoutAFileEveryModIsOn() => Assert.Empty(ModList.Read(_path));

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[1, 2]")]
    [InlineData("{ \"turnedOff\": 5 }")]
    public void ABrokenFileTurnsNothingOff(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, text);
        Assert.Empty(ModList.Read(_path));
    }

    [Fact]
    public void WhatIsWrittenIsReadBackIgnoringCase()
    {
        Assert.True(ModList.Write(_path, ["b.mod", "A.Mod", "a.mod", "gone.mod"]));
        var off = ModList.Read(_path);
        Assert.Equal(3, off.Count); // a.mod once
        Assert.Contains("a.mod", off);
        Assert.Contains("B.MOD", off);
        Assert.Contains("gone.mod", off); // a mod that is not installed stays listed
    }

    [Fact]
    public void TheFileIsSmallSortedJson()
    {
        ModList.Write(_path, ["z.mod", "a.mod"]);
        string text = File.ReadAllText(_path);
        Assert.Contains("\"version\": 1", text);
        Assert.True(text.IndexOf("a.mod", StringComparison.Ordinal) < text.IndexOf("z.mod", StringComparison.Ordinal));
        Assert.False(File.Exists(_path + ".tmp"));
    }

    [Fact]
    public void AFileWrittenByHandIsRead()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ \"TurnedOff\": [ \" spaced.mod \", \"\", null ] }");
        Assert.Equal(["spaced.mod"], ModList.Read(_path));
    }
}
