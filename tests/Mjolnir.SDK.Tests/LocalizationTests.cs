using System;
using System.Collections.Generic;
using System.Linq;
using Mjolnir.SDK.Threading;
using Xunit;

namespace Mjolnir.SDK.Tests;

/// <summary>A stand-in for the game's strings.</summary>
internal sealed class FakeGameStrings(Dictionary<string, string> strings) : IStringsSource
{
    public bool TryGet(string key, out string text) => strings.TryGetValue(key, out text!);
}

/// <summary>
/// Finding a string: this test assembly plays a mod ("test.mod") whose strings it registers, another mod's strings are
/// reached by their full key, the game's through a fake; then formatting with the language's culture, a change of
/// language (announced on the next frame) and bound texts that follow it.
/// </summary>
// The language, the registered strings and the dispatcher are process-wide: one test class at a time.
[Collection("MainThread")]
public sealed class LocalizationTests : IDisposable
{
    public LocalizationTests()
    {
        Dispatcher.ResetForTests();
        Localization.ResetForTests();
        ModStringsRegistry.ResetForTests();
        ModStringsRegistry.Register(LanguageFilesTests.Parse([], ("english", """
            title = "Example"
            greeting = "Hello, {0}!"
            price = "{0:0.0} coins"
            broken = "Hello, {1}!"
            only = "English only"
            [game]
            "common.back" = "Go back"
            """), ("russian", """
            title = "Пример"
            greeting = "Привет, {0}!"
            price = "{0:0.0} монет"
            """)), owner: typeof(LocalizationTests).Assembly);
        ModStringsRegistry.Register(ModStrings.Parse("other.mod", [("english", "english.toml", "title = \"Other\"")], _ => { }));
        Localization.Game = new FakeGameStrings(new() { ["common.settings"] = "Settings", ["title"] = "The game's title" });
    }

    public void Dispose()
    {
        Localization.ResetForTests();
        ModStringsRegistry.ResetForTests();
        Dispatcher.ResetForTests();
    }

    [Fact]
    public void UntilTheGameLoadsItsLanguageItIsEnglish()
    {
        Assert.Equal("english", Localization.Language);
        Assert.Equal("Example", Localization.Get("title"));
    }

    [Fact]
    public void AModsOwnKeyComesFirstThenAFullKeyThenTheGameThenTheKeyItself()
    {
        Localization.LanguageLoaded("russian");
        Assert.Equal("Пример", Localization.Get("title"));             // this mod's, before the game's key of the same name
        Assert.Equal("English only", Localization.Get("only"));        // this mod's English
        Assert.Equal("Other", Localization.Get("other.mod.title"));    // another mod's, by its full key
        Assert.Equal("Пример", Localization.Get("test.mod.title"));    // this mod's full key too
        Assert.Equal("Settings", Localization.Get("common.settings")); // the game's
        Assert.Equal("no.such.key", Localization.Get("no.such.key"));
    }

    [Fact]
    public void WithoutTheGameAModsOverrideStandsForTheGamesString()
    {
        Localization.Game = NoGameStrings.Instance; // another thread, or before the game's manager exists
        Assert.Equal("Go back", Localization.Get("common.back"));
        Assert.Equal("common.settings", Localization.Get("common.settings"));
    }

    [Fact]
    public void ArgumentsAreFormattedWithTheLanguagesCulture()
    {
        Assert.Equal("Hello, Ragnar!", Localization.Get("greeting", "Ragnar"));
        Assert.Equal("1.5 coins", Localization.Get("price", 1.5));
        Localization.LanguageLoaded("russian");
        Assert.Equal("ru-RU", Localization.Culture.Name);
        Assert.Equal("1,5 монет", Localization.Get("price", 1.5));
    }

    [Fact]
    public void AFormatThatDoesNotFitItsArgumentsIsShownAsItIs() =>
        Assert.Equal("Hello, {1}!", Localization.Get("broken", "Ragnar"));

    [Fact]
    public void AChangeOfLanguageIsAnnouncedOnTheNextFrameOnce()
    {
        Dispatcher.MarkMainThread();
        var seen = new List<(string Old, string New, object? Sender)>();
        EventHandler<ValueChangedEventArgs<string>> handler = (s, e) => seen.Add((e.OldValue, e.NewValue, s));
        Localization.LanguageChanged += handler;
        try
        {
            Localization.LanguageLoaded("russian");
            Assert.Equal("russian", Localization.Language); // at once
            Assert.Empty(seen);                              // announced next frame, not inside the game's loading
            Dispatcher.Tick();
            Localization.LanguageLoaded("russian");
            Dispatcher.Tick();
            Assert.Equal([("english", "russian", (object?)null)], seen);
        }
        finally
        {
            Localization.LanguageChanged -= handler;
        }
    }

    [Fact]
    public void ABoundTextFollowsTheLanguage()
    {
        Dispatcher.MarkMainThread();
        var text = Localization.GetBindable("greeting", "Ragnar");
        Assert.Equal("greeting", text.ToString()); // not read yet: ToString never looks the key up
        Assert.Equal("Hello, Ragnar!", text.Value);
        var seen = new List<(string Old, string New)>();
        EventHandler<ValueChangedEventArgs<string>> handler = (_, e) => seen.Add((e.OldValue, e.NewValue));
        text.Changed += handler;

        Localization.LanguageLoaded("russian");
        Dispatcher.Tick();
        Assert.Equal([("Hello, Ragnar!", "Привет, Ragnar!")], seen);
        Assert.Equal("Привет, Ragnar!", text.ToString());

        text.Changed -= handler;
        Localization.LanguageLoaded("english");
        Dispatcher.Tick();
        Assert.Single(seen); // no longer watched
        Assert.Equal("Hello, Ragnar!", text.Value);
    }

    [Fact]
    public void ATextThatFailsToFollowTheLanguageLeavesTheOthersFollowing()
    {
        Dispatcher.MarkMainThread();
        int followed = 0;
        Action throwing = () => throw new InvalidOperationException("a text's handler fault");
        Action counting = () => followed++;
        Localization.Switched += throwing;
        Localization.Switched += counting;
        try
        {
            Localization.LanguageLoaded("russian");
            Dispatcher.Tick();
            Assert.Equal(1, followed);
        }
        finally
        {
            Localization.Switched -= throwing;
            Localization.Switched -= counting;
        }
    }

    [Fact]
    public void AModsStringsAreFoundByItsIdAndTheListIsKeptUntilItChanges()
    {
        Assert.Equal("other.mod", ModStringsRegistry.ForModId("OTHER.MOD")?.ModId);
        Assert.Null(ModStringsRegistry.ForModId("no.such.mod"));
        var all = ModStringsRegistry.All();
        Assert.Same(all, ModStringsRegistry.All());
        ModStringsRegistry.Register(ModStrings.Parse("third.mod", [("english", "english.toml", "title = \"Third\"")], _ => { }));
        Assert.NotSame(all, ModStringsRegistry.All());
        Assert.Contains(ModStringsRegistry.All(), strings => strings.ModId == "third.mod");
    }

    [Fact]
    public void AnAssemblyThatIsNoModFindsFullKeysAndTheGamesOnly()
    {
        var notAMod = ModStringsRegistry.ForAssembly(typeof(object).Assembly);
        Assert.Null(notAMod);
        Assert.Equal("Example", Localization.Resolve(null, "test.mod.title", []));
        Assert.Equal("The game's title", Localization.Resolve(null, "title", []));
    }

    // Every mod with strings, by id (the loaded mods of other test classes are in ModRegistry too).
    [Fact]
    public void TheModsOfTheGamesDictionaryAreTheRegisteredModsById()
    {
        var ids = ModStringsRegistry.All().Select(s => s.ModId).ToList();
        Assert.Contains("other.mod", ids);
        Assert.Contains("test.mod", ids);
        Assert.Equal(ids.OrderBy(id => id, StringComparer.Ordinal), ids);
    }
}
