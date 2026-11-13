using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Mjolnir.SDK.Tests;

/// <summary>
/// A mod's language files: what a file name means (Steam's names and the game's codes), what a file holds (nested tables
/// with dots, the [game] table of overrides, non-string values, a file that does not parse), English as the fallback,
/// what goes into the game's dictionaries, and reading a lang folder.
/// </summary>
public class LanguageFilesTests
{
    [Theory]
    [InlineData("russian", "russian")]
    [InlineData("Russian", "russian")]
    [InlineData("ru", "russian")]
    [InlineData("ru-RU", "russian")]
    [InlineData("RU-ru", "russian")]
    [InlineData("en", "english")]
    [InlineData("en-US", "english")] // the game's code list gives en-US to english
    [InlineData("en-GB", "english")]
    [InlineData("englishUS", "englishUS")]
    [InlineData("zh-Hans", "schinese")]
    [InlineData("zh-Hant", "tchinese")]
    [InlineData("pt-PT", "brazilian")]
    [InlineData("es-419", "spanish")]
    [InlineData("Klingon", "klingon")] // not a game language: kept by name
    public void AFileNameNamesALanguage(string fileName, string language) => Assert.Equal(language, GameLanguages.FromFileName(fileName));

    [Theory]
    [InlineData("russian", "ru-RU")]
    [InlineData("english", "en-US")]
    [InlineData("schinese", "zh-Hans")]
    [InlineData("klingon", "")]
    public void ALanguageHasACulture(string language, string culture) => Assert.Equal(culture, GameLanguages.CultureOf(language).Name);

    [Fact]
    public void TablesNestWithDotsAndTheGameTableOverridesTheGamesStrings()
    {
        var warnings = new List<string>();
        var strings = Parse(warnings, ("russian", """
            title = "Пример"
            [hud]
            greeting = "Привет, {0}!"
            [hud.clock]
            label = "Часы"
            [game]
            "common.settings" = "Настройки игры"
            """));

        Assert.True(strings.TryGet("title", "russian", out string title));
        Assert.Equal("Пример", title);
        Assert.True(strings.TryGet("hud.greeting", "russian", out _));
        Assert.True(strings.TryGet("hud.clock.label", "russian", out string label));
        Assert.Equal("Часы", label);
        Assert.False(strings.TryGet("game.common.settings", "russian", out _));
        Assert.True(strings.TryGetOverride("common.settings", "russian", out string settings));
        Assert.Equal("Настройки игры", settings);
        Assert.Empty(warnings);
    }

    [Fact]
    public void EnglishIsTheFallbackForKeysButNotForOverrides()
    {
        var strings = Parse([], ("english", """
            title = "Example"
            only = "English only"
            [game]
            "common.settings" = "Game settings"
            """), ("russian", "title = \"Пример\""));

        Assert.True(strings.TryGet("only", "russian", out string only));
        Assert.Equal("English only", only);
        Assert.True(strings.TryGet("title", "german", out string title));
        Assert.Equal("Example", title);
        // The game's own Russian stays: the mod did not translate its override.
        Assert.False(strings.TryGetOverride("common.settings", "russian", out _));
        Assert.True(strings.TryGetOverride("common.settings", "english", out _));
    }

    [Fact]
    public void NonStringsAndFilesThatDoNotParseAreSkippedWithAWarning()
    {
        var warnings = new List<string>();
        var strings = Parse(warnings, ("english", "count = 3\ntitle = \"Example\""), ("russian", "title = \"broken"), ("klingon", "title = \"nuqneH\""));

        Assert.False(strings.TryGet("count", "english", out _));
        Assert.True(strings.TryGet("title", "english", out _));
        Assert.Contains(warnings, w => w.Contains("'count' is not a string"));
        Assert.Contains(warnings, w => w.Contains("russian.toml skipped: line"));
        Assert.Contains(warnings, w => w.Contains("'klingon' is not one of the game's languages"));
    }

    [Fact]
    public void TheGamesDictionaryGetsPrefixedKeysInItsLanguageOrEnglishAndOverridesOfThatLanguage()
    {
        var strings = Parse([], ("english", """
            title = "Example"
            only = "English only"
            [game]
            "common.back" = "Go back"
            """), ("russian", """
            title = "Пример"
            [game]
            "common.settings" = "Настройки игры"
            """));

        Assert.Equal(
            [new("test.mod.only", "English only"), new("test.mod.title", "Пример"), new("common.settings", "Настройки игры")],
            strings.EntriesFor("russian").ToArray());
        Assert.Equal(
            [new("test.mod.only", "English only"), new("test.mod.title", "Example"), new("common.back", "Go back")],
            strings.EntriesFor("english").ToArray());
    }

    [Fact]
    public void ALangFolderIsReadByFileName()
    {
        string root = Path.Combine(Path.GetTempPath(), "mjolnir-lang-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "lang"));
        try
        {
            File.WriteAllText(Path.Combine(root, "lang", "en.toml"), "title = \"Example\"");
            File.WriteAllText(Path.Combine(root, "lang", "ru-RU.toml"), "title = \"Пример\"");
            File.WriteAllText(Path.Combine(root, "lang", "notes.txt"), "not a language file");
            var strings = ModStrings.Load("test.mod", root, _ => { });

            Assert.Equal(["english", "russian"], strings.Languages.OrderBy(l => l));
            Assert.True(strings.TryGet("title", "russian", out string title));
            Assert.Equal("Пример", title);
            Assert.Empty(ModStrings.Load("test.mod", Path.Combine(root, "nowhere"), _ => { }).Languages);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    internal static ModStrings Parse(List<string> warnings, params (string Language, string Text)[] files) =>
        ModStrings.Parse("test.mod", files.Select(f => (f.Language, f.Language + ".toml", f.Text)), warnings.Add);
}
