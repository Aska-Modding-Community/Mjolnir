using System;
using Xunit;
using static Mjolnir.SDK.Net.Tests.CompatibilityTests;

namespace Mjolnir.SDK.Net.Tests;

/// <summary>What a refused client reads: a title, then each difference by mod name and version, then what to do.</summary>
public class RefusalTextTests
{
    // The keys themselves, with their arguments, so the test reads the layout and not a language.
    private static string Key(string key, object?[] args) => args.Length == 0 ? key : $"{key}({string.Join(", ", args)})";

    [Fact]
    public void DifferencesAreListedByNameAndVersion()
    {
        var report = CompatibilityReport.Compare("0.3.0", [Shared("items"), Shared("stats", "2.0.0")], "0.3.0",
                                                 [Shared("stats", "1.0.0"), Shared("cheats")]);
        var (title, message) = NetTexts.Refusal(report, Key);
        Assert.Equal("refusal.title", title);
        Assert.Equal("""
                     refusal.differs

                     refusal.missing
                       • Items Mod 1.0.0

                     refusal.extra
                       • Cheats Mod 1.0.0

                     refusal.versions
                       • refusal.version(Stats Mod, 2.0.0, 1.0.0)

                     refusal.action
                     """.ReplaceLineEndings("\n"), message);
    }

    [Fact]
    public void AnotherMjolnirVersionNamesBoth()
    {
        var (_, message) = NetTexts.Refusal(CompatibilityReport.Compare("0.4.0", [], "0.3.0", []), Key);
        Assert.Equal("refusal.differs\n\nrefusal.platform(0.4.0, 0.3.0)", message);
    }

    [Fact]
    public void AHostWithoutMjolnirListsWhatToTurnOff()
    {
        var (_, message) = NetTexts.Refusal(CompatibilityReport.HostHasNoMjolnir("0.3.0", [Shared("items")]), Key);
        Assert.Equal("refusal.vanilla\n  • Items Mod 1.0.0\n\nrefusal.action", message);
    }

    [Fact]
    public void NoAnswerAsksToTryAgain() =>
        Assert.Equal("refusal.no_answer", NetTexts.Refusal(CompatibilityReport.NoAnswer("0.3.0", "0.3.0"), Key).Message);

    [Fact]
    public void TheTextsExistInEnglishAndRussian()
    {
        var english = NetTexts.Refusal(CompatibilityReport.Compare("0.3.0", [Shared("items")], "0.3.0", []));
        Assert.DoesNotContain("refusal.", english.Message);
        Assert.False(string.IsNullOrWhiteSpace(english.Title));
        var strings = typeof(NetTexts).GetField("s_strings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null) as Lazy<ModStrings>;
        Assert.Contains("russian", strings!.Value.Languages);
    }
}
