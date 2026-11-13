using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mjolnir.SDK.Config;
using Xunit;

namespace Mjolnir.SDK.Tests;

public sealed class ModConfigTests : IDisposable
{
    private enum Mode { Off, Slow, Fast }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mjolnir-config-tests", Guid.NewGuid().ToString("N"));
    private readonly List<string> _warnings = [];

    public ModConfigTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string File_ => Path.Combine(_dir, "test.mod.toml");

    private ModConfig Load() => ModConfig.Load(File_, "Test Mod (test.mod) 1.0.0", _warnings.Add);

    [Fact]
    public void MetadataIsForTheModsWindowAndTheFileKeepsTheKey()
    {
        var config = Load();
        var plain = config.Bind("General", "Plain", 1);
        var shown = config.Bind("General", "ShowText", true, "Shows the line.")
                          .WithDisplayName("  Show the line ").WithOrder(-1).WithRestartRequired();
        Assert.Throws<ArgumentException>(() => plain.WithDisplayName(" "));
        Assert.Equal((null, 0, false), (plain.DisplayName, plain.Order, plain.IsRestartRequired));
        Assert.Equal(("Show the line", -1, true), (shown.DisplayName, shown.Order, shown.IsRestartRequired));
        config.SaveIfChanged();

        string text = File.ReadAllText(File_);
        Assert.Contains("ShowText = true", text);
        Assert.DoesNotContain("Show the line", text);
        Assert.Contains("# Shows the line.\r\n# Takes effect after the game restarts.\r\n# Default: true", text);
        Assert.Equal(1, text.Split("restarts").Length - 1); // only the entry that needs it
    }

    [Fact]
    public void MissingFileIsWrittenWithDefaultsAndDescriptions()
    {
        var config = Load();
        Assert.True(config.Bind("General", "Enabled", true, "Turns the mod on or off.").Value);
        Assert.Equal(0.3f, config.Bind("General", "Multiplier", 0.3f).Value);
        config.SaveIfChanged();

        string text = File.ReadAllText(File_);
        Assert.Contains("[General]", text);
        Assert.Contains("# Turns the mod on or off.", text);
        Assert.Contains("# Default: true", text);
        Assert.Contains("Multiplier = 0.3", text);
        Assert.Contains("\r\n", text);

        var reloaded = Load();
        Assert.True(reloaded.Bind("General", "Enabled", false).Value);
        Assert.Equal(0.3f, reloaded.Bind("General", "Multiplier", 1f).Value);
        Assert.Empty(_warnings);
    }

    [Fact]
    public void CompleteFileIsReadAndNotRewritten()
    {
        const string text = "# my notes\n[General]\nEnabled = false # keep this comment\nMultiplier = 2\n";
        File.WriteAllText(File_, text);
        var config = Load();
        Assert.False(config.Bind("General", "Enabled", true).Value);
        Assert.Equal(2f, config.Bind("General", "Multiplier", 1f).Value);   // integer accepted for float
        config.SaveIfChanged();
        Assert.Equal(text, File.ReadAllText(File_));
    }

    [Fact]
    public void MissingKeyRewritesAndKeepsUnknownKeysAndSections()
    {
        File.WriteAllText(File_, "top = 1\n[General]\nEnabled = false\nOld = \"x\"\n[Other]\nlist = [1, 2]\n");
        var config = Load();
        config.Bind("General", "Enabled", true);
        config.Bind("General", "New", 5, "Added later.");
        config.SaveIfChanged();

        var reread = Load();
        Assert.False(reread.Bind("General", "Enabled", true).Value);
        Assert.Equal(5, reread.Bind("General", "New", 0).Value);
        Assert.Equal("x", reread.Bind("General", "Old", "default").Value);
        Assert.Empty(_warnings);   // the rewritten file parses
        string text = File.ReadAllText(File_);
        Assert.StartsWith("# Test Mod", text);
        Assert.Contains("top = 1", text);
        Assert.Contains("[Other]", text);
        Assert.Contains("list = [1, 2]", text);
        Assert.True(text.IndexOf("top = 1", StringComparison.Ordinal) < text.IndexOf("[General]", StringComparison.Ordinal));
    }

    [Fact]
    public void InvalidValueFallsBackToDefaultWithAWarning()
    {
        File.WriteAllText(File_, "[General]\nEnabled = \"yes\"\nCount = 99999999999\n");
        var config = Load();
        Assert.True(config.Bind("General", "Enabled", true).Value);
        Assert.Equal(3, config.Bind("General", "Count", 3).Value);   // out of int range
        Assert.Equal(2, _warnings.Count);
        config.SaveIfChanged();
        Assert.True(Load().Bind("General", "Enabled", false).Value);
    }

    [Fact]
    public void AFileThatCannotBeWrittenWarnsAndIsWrittenAtTheNextSave()
    {
        var config = Load();
        config.Bind("General", "Enabled", true);
        File.WriteAllText(File_, "");
        File.SetAttributes(File_, FileAttributes.ReadOnly);
        try
        {
            config.SaveIfChanged(); // a mod's load must not fail over its config file
            Assert.Contains(_warnings, w => w.Contains("could not be saved"));
            Assert.Equal("", File.ReadAllText(File_));
            Assert.False(File.Exists(File_ + ".tmp"));
        }
        finally
        {
            File.SetAttributes(File_, FileAttributes.Normal);
        }
        config.SaveIfChanged(); // still marked changed
        Assert.Contains("Enabled = true", File.ReadAllText(File_));
    }

    [Fact]
    public void SyntaxErrorUsesDefaultsAndNeverWritesTheFile()
    {
        const string text = "[General]\nEnabled = tru\n";
        File.WriteAllText(File_, text);
        var config = Load();
        Assert.Contains(_warnings, w => w.Contains("line 2"));
        Assert.True(config.Bind("General", "Enabled", true).Value);
        config.SaveIfChanged();
        config.Save();
        Assert.Equal(text, File.ReadAllText(File_));
    }

    [Fact]
    public void EnumsStringsAndQuotedNamesRoundTrip()
    {
        var config = Load();
        config.Bind("Odd Section", "Mode", Mode.Slow, "Speed.");
        config.Bind("Odd Section", "Path", "C:\\mods\\\"x\"");
        config.SaveIfChanged();
        Assert.Contains("[\"Odd Section\"]", File.ReadAllText(File_));
        Assert.Contains("# Allowed: Off, Slow, Fast", File.ReadAllText(File_));

        File.WriteAllText(File_, File.ReadAllText(File_).Replace("Mode = \"Slow\"", "Mode = \"fast\""));
        var reread = Load();
        Assert.Equal(Mode.Fast, reread.Bind("Odd Section", "Mode", Mode.Off).Value);
        Assert.Equal("C:\\mods\\\"x\"", reread.Bind("Odd Section", "Path", "").Value);
        Assert.Empty(_warnings);
    }

    [Fact]
    public void SettingAValueMarksTheConfigChanged()
    {
        var config = Load();
        var entry = config.Bind("General", "Count", 1);
        config.SaveIfChanged();
        entry.Value = 7;
        config.SaveIfChanged();
        Assert.Equal(7, Load().Bind("General", "Count", 0).Value);
    }

    [Fact]
    public void BindingTwiceReturnsTheSameEntry()
    {
        var config = Load();
        var first = config.Bind("General", "Count", 1);
        Assert.Same(first, config.Bind("General", "Count", 2));
        Assert.Throws<InvalidOperationException>(() => config.Bind("General", "Count", "text"));
    }

    [Fact]
    public void UnsupportedTypesAreRejected()
    {
        var config = Load();
        Assert.Throws<NotSupportedException>(() => config.Bind("General", "When", DateTime.Now));
    }

    [Fact]
    public void RangeClampsTheFileValueAndIsWrittenAsAComment()
    {
        File.WriteAllText(File_, "[General]\nCount = 50\n");
        var config = Load();
        var count = config.Bind("General", "Count", 5).WithRange(0, 10);
        Assert.Equal(10, count.Value);
        Assert.Contains(_warnings, w => w.Contains("outside 0 .. 10"));
        config.SaveIfChanged();

        string text = File.ReadAllText(File_);
        Assert.Contains("# Range: 0 .. 10", text);
        Assert.Contains("Count = 10", text);
    }

    [Fact]
    public void RangeClampsValuesSetFromCode()
    {
        var multiplier = Load().Bind("General", "Multiplier", 1f).WithRange(0.5f, 2f);
        multiplier.Value = 7f;
        Assert.Equal(2f, multiplier.Value);
        multiplier.Value = -1f;
        Assert.Equal(0.5f, multiplier.Value);
    }

    [Fact]
    public void RangeNeedsANumberAndAValidDefault()
    {
        var config = Load();
        Assert.Throws<NotSupportedException>(() => config.Bind("General", "Name", "x").WithRange("a", "z"));
        Assert.Throws<ArgumentException>(() => config.Bind("General", "A", 1).WithRange(5, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => config.Bind("General", "B", 20).WithRange(0, 10));
    }

    [Fact]
    public void ChangedIsRaisedForRealChangesWithTheStoredValue()
    {
        var count = Load().Bind("General", "Count", 1).WithRange(0, 10);
        var seen = new List<(object? Sender, int Old, int New)>();
        count.Changed += (s, e) => seen.Add((s, e.OldValue, e.NewValue));
        count.Value = 1;  // same value: no event
        count.Value = 4;
        count.Value = 99; // clamped before the event
        count.Value = 10; // equal after clamping: no event
        Assert.Equal([(count, 1, 4), (count, 4, 10)], seen);
    }

    [Fact]
    public void EntriesKeepBindingOrderForTheSettingsWindow()
    {
        var config = Load();
        config.Bind("B", "One", 1);
        config.Bind("A", "Two", true);
        config.Bind("B", "Three", Mode.Fast);
        Assert.Equal(["One", "Two", "Three"], config.Entries.Select(e => e.Key));
        Assert.Equal(Mode.Fast, config.Entries[2].BoxedDefault);
    }
}
