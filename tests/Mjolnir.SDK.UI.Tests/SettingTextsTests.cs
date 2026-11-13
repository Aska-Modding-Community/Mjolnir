using System;
using System.IO;
using System.Linq;
using Mjolnir.SDK.Config;
using Xunit;

namespace Mjolnir.SDK.UI.Tests;

/// <summary>
/// A mod's settings in the player's language from its lang files: a row's name and description
/// ([config.&lt;section&gt;.&lt;key&gt;]), a section's header ([sections]), English as the fallback, the display name
/// and key without a translation, the restart note after a translated description, the row's object name the same in
/// every language, and SettingControls.For finding the calling mod's strings.
/// </summary>
[Collection("MainThread")]
public sealed class SettingTextsTests : ViewTestBase, IDisposable
{
    private static readonly ModStrings Strings = ModStrings.Parse("test.mod", [("english", "english.toml", """
        [sections]
        Keys = "Keys"
        """), ("russian", "russian.toml", """
        [sections]
        HUD = "Строка в HUD"

        [config.HUD.ShowText]
        name = "Показывать строку"
        description = "Строка примера в HUD."

        [config.HUD.Restart]
        description = "Нужен перезапуск."

        [mod]
        name = "Пример"
        description = "Все элементы."
        """)], _ => { });

    private readonly ModConfig _config;

    public SettingTextsTests()
    {
        Localization.ResetForTests();
        ModStringsRegistry.ResetForTests();
        _config = ModConfig.Load(Path.Combine(Path.GetTempPath(), "mjolnir-ui-tests", Guid.NewGuid().ToString("N") + ".toml"), "Test", _ => { });
    }

    void IDisposable.Dispose()
    {
        Localization.ResetForTests();
        ModStringsRegistry.ResetForTests();
        Dispose();
    }

    [Fact]
    public void ARowsNameAndDescriptionComeFromTheModsStringsInThePlayersLanguage()
    {
        var entry = _config.Bind("HUD", "ShowText", true, "Show the line.").WithDisplayName("Show the HUD line");
        var english = SettingControls.For(entry, Strings);
        Assert.Equal("Show the HUD line", english.Header); // no English translation: the display name
        Assert.Equal("Show the line.", english.Description);

        Localization.LanguageLoaded("russian");
        var russian = SettingControls.For(entry, Strings);
        Assert.Equal("Показывать строку", russian.Header);
        Assert.Equal("Строка примера в HUD.", russian.Description);
        Assert.Equal("Setting HUD.ShowText", russian.Name); // the same in every language
        Assert.Equal(english.Name, russian.Name);
    }

    [Fact]
    public void ATranslatedDescriptionStillGetsTheRestartNoteAndAMissingNameTheKey()
    {
        Localization.LanguageLoaded("russian");
        var entry = _config.Bind("HUD", "Restart", 1, "Needs a restart.").WithRestartRequired();
        var row = SettingControls.For(entry, Strings);
        Assert.Equal("Restart", row.Header);
        Assert.Equal("Нужен перезапуск. (применится после перезапуска)", row.Description);
    }

    [Fact]
    public void SectionsAreTranslatedAndEnglishIsTheFallback()
    {
        _config.Bind("HUD", "ShowText", true);
        _config.Bind("Keys", "Window", KeyGesture.Parse("F7"));
        _config.Bind("Other", "Thing", "x");
        Localization.LanguageLoaded("russian");
        var page = ModsWindow.SettingsContent(_config, Strings);
        var headers = page.Children.OfType<UiSectionHeader>().Select(h => h.Text);
        Assert.Equal(["Строка в HUD", "Keys", "Other"], headers);
    }

    [Fact]
    public void AModsNameAndDescriptionAreTranslated()
    {
        Assert.Equal("Example", SettingTexts.ModNameOf("Example", Strings));
        Localization.LanguageLoaded("russian");
        Assert.Equal("Пример", SettingTexts.ModNameOf("Example", Strings));
        Assert.Equal("Все элементы.", SettingTexts.ModDescriptionOf("Every element.", Strings));
        Assert.Null(SettingTexts.ModDescriptionOf(null, null));
    }

    [Fact]
    public void SettingControlForFindsTheCallingModsStrings()
    {
        // This test assembly plays the mod that calls SettingControls.For.
        ModStringsRegistry.Register(Strings, owner: typeof(SettingTextsTests).Assembly);
        Localization.LanguageLoaded("russian");
        var entry = _config.Bind("HUD", "ShowText", true);
        Assert.Equal("Показывать строку", SettingControls.For(entry).Header);
    }
}
