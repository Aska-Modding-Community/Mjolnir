using System;
using System.ComponentModel;
using System.Linq;
using Mjolnir.SDK.UI;
using Xunit;

namespace Mjolnir.SDK.UI.Tests;

/// <summary>
/// What the player sees: a disabled panel disables and dims what is in it once (IsEffectivelyEnabled), the texts of a
/// selector's items (ItemText, an enum's names in the player's language or its [Description]), and the SDK's own texts
/// in the player's language.
/// </summary>
[Collection("MainThread")]
public sealed class PlayerFacingTests : ViewTestBase, IDisposable
{
    private enum Season
    {
        Spring,
        [Description("Long winter")] LongWinter,
        EndlessNight,
    }

    public PlayerFacingTests()
    {
        Localization.ResetForTests();
        ModStringsRegistry.ResetForTests();
    }

    void IDisposable.Dispose()
    {
        Localization.ResetForTests();
        ModStringsRegistry.ResetForTests();
        Dispose();
    }

    [Fact]
    public void ADisabledPanelDisablesWhatIsInItThoughTheirOwnIsEnabledStaysTrue()
    {
        var button = new UiButton { Content = "Go" };
        var inner = new UiStackPanel { Children = { button } };
        var panel = new UiStackPanel { Children = { inner } };
        Assert.True(button.IsEffectivelyEnabled);

        panel.IsEnabled = false;
        Assert.False(button.IsEffectivelyEnabled);
        Assert.False(inner.IsEffectivelyEnabled);
        Assert.True(button.IsEnabled);

        panel.IsEnabled = true;
        Assert.True(button.IsEffectivelyEnabled);
    }

    [Fact]
    public void ACommandThatCannotExecuteMakesItsButtonNotEffectivelyEnabled()
    {
        var button = new UiButton { Command = new Command(() => { }, () => false) };
        Assert.True(button.IsEnabled);
        Assert.False(button.IsEffectivelyEnabled);
    }

    [Fact]
    public void OnlyTheOutermostDisabledElementIsDimmed()
    {
        var enabled = new UiButton { Content = "On" };
        var disabled = new UiButton { Content = "Off", IsEnabled = false };
        var panel = new UiStackPanel { Children = { enabled, disabled } };
        Assert.Equal((false, true), disabled.EnabledLook); // alone, it dims itself

        panel.IsEnabled = false;
        Assert.Equal((false, true), panel.EnabledLook);
        Assert.Equal((false, false), disabled.EnabledLook); // the panel's dimming reaches it: not twice
        Assert.Equal((true, false), enabled.EnabledLook);    // blocked by the panel's group, not by its own
    }

    [Fact]
    public void DisablingAPanelShowsItsChildrenAgain()
    {
        var disabled = new UiButton { Content = "Off", IsEnabled = false };
        var panel = new UiStackPanel { Children = { new UiStackPanel { Children = { disabled } } } };
        Mount(panel);
        var view = ViewOf(disabled);
        int applied = view.AppliedCount(DirtyFlags.Enabled);

        panel.IsEnabled = false;
        Assert.Equal(applied + 1, view.AppliedCount(DirtyFlags.Enabled));
    }

    [Fact]
    public void AnItemTextFunctionMakesTheItemsTexts()
    {
        var selector = new UiStepper { ItemsSource = new[] { 1, 2 }, ItemText = item => $"#{item}" };
        Assert.Equal(["#1", "#2"], selector.ItemTexts);
    }

    [Fact]
    public void AThrowingItemTextShowsTheDefaultText()
    {
        var selector = new UiStepper { ItemsSource = new[] { 1 }, ItemText = _ => throw new InvalidOperationException("broken") };
        Assert.Equal(["1"], selector.ItemTexts);
    }

    [Fact]
    public void AnEnumShowsItsDescriptionOrItsName()
    {
        var selector = new UiComboBox { ItemsSource = Enum.GetValues<Season>() };
        Assert.Equal(["Spring", "Long winter", "EndlessNight"], selector.ItemTexts);
    }

    [Fact]
    public void AnEnumShowsTheNamesItsModGivesItInThePlayersLanguage()
    {
        // This test assembly plays the mod that declares Season, with its strings in Russian only.
        ModStringsRegistry.Register(ModStrings.Parse("test.mod", [("russian", "russian.toml", """
            [Season]
            Spring = "Весна"
            LongWinter = "Долгая зима"
            """)], _ => { }), owner: typeof(PlayerFacingTests).Assembly);
        var selector = new UiComboBox { ItemsSource = Enum.GetValues<Season>() };
        Assert.Equal(["Spring", "Long winter", "EndlessNight"], selector.ItemTexts);

        Localization.LanguageLoaded("russian");
        Assert.Equal(["Весна", "Долгая зима", "EndlessNight"], selector.ItemTexts);
    }

    [Fact]
    public void AChangeOfLanguageShowsALiveSelectorsItemsAgain()
    {
        var selector = new UiComboBox { ItemsSource = Enum.GetValues<Season>() };
        var view = Mount(selector);
        int applied = view.AppliedCount(DirtyFlags.Options);

        Localization.LanguageLoaded("russian");
        Tick(); // the change is announced on the next frame, then the copy follows
        Tick();
        Assert.True(view.AppliedCount(DirtyFlags.Options) > applied);
    }

    [Fact]
    public void TheSdksOwnTextsFollowThePlayersLanguage()
    {
        Assert.Equal("Keep it (applies after a restart)", SettingControls.DescriptionOf("Keep it\nmore lines", restartRequired: true));
        Assert.Equal("Applies after a restart.", SettingControls.DescriptionOf(null, restartRequired: true));
        Assert.Equal("OK", SdkTexts.Get("dialog.ok"));

        Localization.LanguageLoaded("russian");
        Assert.Equal("Keep it (применится после перезапуска)", SettingControls.DescriptionOf("Keep it", restartRequired: true));
        Assert.Equal("ОК", SdkTexts.Get("dialog.ok"));
        Assert.Equal("Вернуть все настройки этого мода к значениям по умолчанию?", SdkTexts.Get("mods.reset"));

        Localization.LanguageLoaded("englishUS"); // no file of its own: English
        Assert.Equal("Restart the game to apply the changed settings.", SdkTexts.Get("mods.restart"));
    }

    [Fact]
    public void EverySdkTextHasARussianTranslation()
    {
        string[] keys = ["mods.disabled", "mods.reset", "mods.restart", "mods.apply", "mods.none", "mods.off", "sections.UI", "config.UI.Menus.name", "config.UI.Menus.description", "config.UI.Hud.name", "config.UI.Hud.description", "mods.turns_off", "mods.turns_on", "mods.failed",
                         "mods.not_loaded", "mods.required_by", "mods.required_by_many", "mods.requires", "mods.requires_many", "mods.turn_off",
                         "mods.turn_on_too", "mods.only_this", "mods.reason.duplicate", "mods.reason.missing", "mods.reason.wrong_version",
                         "mods.reason.dependency_not_loaded", "mods.reason.dependency_turned_off", "mods.reason.dependency_failed",
                         "mods.reason.self_dependency", "mods.reason.cycle", "setting.restart", "setting.restart_alone", "dialog.ok", "dialog.cancel"];
        var english = keys.Select(key => SdkTexts.Get(key, "x")).ToArray();
        Localization.LanguageLoaded("russian");
        var russian = keys.Select(key => SdkTexts.Get(key, "x")).ToArray();
        Assert.All(keys.Zip(english), t => Assert.NotEqual(t.First, t.Second)); // found: a missing key comes back as itself
        Assert.All(keys.Zip(english, russian), t => Assert.NotEqual(t.Second, t.Third));
    }
}
