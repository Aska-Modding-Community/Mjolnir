using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mjolnir.SDK.Threading;

namespace Mjolnir.SDK;

/// <summary>
/// Text in the player's language. A mod's strings live in its <c>lang</c> folder, a file per language next to its .dll:
/// <c>lang/english.toml</c>, <c>lang/russian.toml</c> (Steam's language names, or codes such as <c>ru</c> or
/// <c>ru-RU</c>). English is the fallback. The SDK also puts them into the game's own dictionaries as
/// <c>&lt;mod id&gt;.&lt;key&gt;</c>, and a file's <c>[game]</c> table overrides the game's strings by their keys.
/// </summary>
/// <example><code>
/// # lang/russian.toml
/// [hud]
/// greeting = "Привет, {0}!"
///
/// [game]
/// "common.settings" = "Настройки игры"
/// </code><code>
/// Notify.Message(Localization.Get("hud.greeting", LocalPlayer.Name));
/// header.Bind(UiSectionHeader.TextProperty, Localization.GetBindable("hud.title")); // follows a change of language
/// </code></example>
/// <remarks>
/// The game takes its language from Steam (the game's properties), so it stays the same for a session; until the game
/// has loaded it (in the main menu) <see cref="Language"/> is English. Game strings are read on the main thread; on
/// another thread only mods' strings are found. The SDK's content module (Mjolnir.SDK.Content) connects all this to the
/// game; without it the language stays English and only mods' strings are found.
/// </remarks>
public static class Localization
{
    internal const string Source = "SDK.Localization";

    private static readonly ModHandlers<EventHandler<ValueChangedEventArgs<string>>> s_languageChanged = new("Localization.LanguageChanged");
    private static readonly HashSet<string> s_badFormats = [];
    private static readonly object s_switchLock = new();
    private static Action? s_switched;
    private static volatile string s_language = GameLanguages.Fallback;

    /// <summary>
    /// The game's language, by Steam's name: <c>english</c>, <c>russian</c>, <c>german</c>, <c>schinese</c>,
    /// <c>brazilian</c>…; English until the game has loaded its language.
    /// </summary>
    public static string Language => s_language;

    /// <summary>The culture of <see cref="Language"/> (ru-RU for russian): <see cref="Get"/> formats numbers with it.</summary>
    public static CultureInfo Culture => GameLanguages.CultureOf(s_language);

    /// <summary>
    /// Raised on the main thread when the game's language changes, at the start of the frame after the game loaded the
    /// new one (also once at the start, when the game loads its language over the English default). A static event: the
    /// sender is null.
    /// </summary>
    public static event EventHandler<ValueChangedEventArgs<string>>? LanguageChanged
    {
        add => s_languageChanged.Add(value);
        remove => s_languageChanged.Remove(value);
    }

    /// <summary>
    /// The text of <paramref name="key"/> in the player's language, formatted with <paramref name="args"/> (as
    /// <see cref="string.Format(IFormatProvider, string, object[])"/> with <see cref="Culture"/>). Looked up in your
    /// mod's strings (else in English), then as a full key of any mod (<c>&lt;mod id&gt;.&lt;key&gt;</c>), then among
    /// the game's own strings (<c>common.settings</c>); a key found nowhere comes back as it is.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string Get(string key, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Resolve(ModStringsRegistry.ForAssembly(CallingMod.Of(Assembly.GetCallingAssembly())), key, args ?? []);
    }

    /// <summary>
    /// <paramref name="key"/>'s text as a binding source that follows a change of language: <c>text.Bind(UiTextBlock.TextProperty,
    /// Localization.GetBindable("hud.title"))</c>. Found as <see cref="Get"/> finds it, for your mod.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static TranslatedText GetBindable(string key, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new TranslatedText(ModStringsRegistry.ForAssembly(CallingMod.Of(Assembly.GetCallingAssembly())), key, args ?? []);
    }

    /// <summary>The game's strings (the content module's GameStrings once it started); a test puts a fake here.</summary>
    internal static IStringsSource Game { get; set; } = NoGameStrings.Instance;

    /// <summary>Raised (internally, before <see cref="LanguageChanged"/>) when the language changed: bound texts re-read.</summary>
    internal static event Action? Switched
    {
        add
        {
            lock (s_switchLock) s_switched += value;
        }
        remove
        {
            lock (s_switchLock) s_switched -= value;
        }
    }

    /// <summary>
    /// A string of the mod that <paramref name="assembly"/> belongs to, in the player's language or else in English; no
    /// other lookup (the SDK's modules: an enum's item names from the mod that declares the enum).
    /// </summary>
    internal static bool TryGetModString(Assembly assembly, string key, out string text)
    {
        if (ModStringsRegistry.ForAssembly(assembly) is { } strings) return strings.TryGet(key, s_language, out text);
        text = "";
        return false;
    }

    internal static string Resolve(ModStrings? owner, string key, object?[] args)
    {
        string language = s_language;
        if (!(owner != null && owner.TryGet(key, language, out string text)) &&
            !ModStringsRegistry.TryGetFullKey(key, language, out text) &&
            !Game.TryGet(key, out text) &&
            !ModStringsRegistry.TryGetOverride(key, language, out text))
            text = key;
        return args.Length == 0 ? text : Format(key, text, args);
    }

    /// <summary>
    /// The game loaded <paramref name="language"/> into its main dictionary (the content module's patch, main thread): it
    /// is the language from now on, announced at the start of the next frame, not inside the game's loading.
    /// </summary>
    internal static void LanguageLoaded(string language)
    {
        string old = s_language;
        if (string.Equals(old, language, StringComparison.OrdinalIgnoreCase)) return;
        s_language = language;
        Dispatcher.MainThread.Post(() =>
        {
            Action? switched;
            lock (s_switchLock) switched = s_switched;
            // One at a time: a texts' handler that throws is logged and the others still follow the language.
            ModCall.InvokeEach(switched, "Localization.Switched");
            s_languageChanged.Raise(h => h(null, new ValueChangedEventArgs<string>(old, language)));
        });
    }

    /// <summary>Unit tests: back to English and no game.</summary>
    internal static void ResetForTests()
    {
        s_language = GameLanguages.Fallback;
        Game = NoGameStrings.Instance;
        lock (s_badFormats) s_badFormats.Clear();
    }

    private static string Format(string key, string text, object?[] args)
    {
        try
        {
            return string.Format(Culture, text, args);
        }
        catch (FormatException e)
        {
            bool first;
            lock (s_badFormats) first = s_badFormats.Add(key);
            if (first) SdkLog.Warn(Localization.Source, $"'{key}' does not format with {args.Length} argument(s), shown as it is: {e.Message}");
            return text;
        }
    }
}

/// <summary>The game's own strings, behind an interface so that the model is tested without the game.</summary>
internal interface IStringsSource
{
    /// <summary>The game's text for <paramref name="key"/> if the game has the key (main thread only; false elsewhere).</summary>
    bool TryGet(string key, out string text);
}

/// <summary>No game: before the module started, and in unit tests.</summary>
internal sealed class NoGameStrings : IStringsSource
{
    public static readonly NoGameStrings Instance = new();

    public bool TryGet(string key, out string text)
    {
        text = "";
        return false;
    }
}
