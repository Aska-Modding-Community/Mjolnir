using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mjolnir.SDK.Config;

namespace Mjolnir.SDK.UI;

/// <summary>
/// The SDK's own Mods window, behind a button in the main menu and the pause menu, both labelled with the platform's
/// name (<see cref="PlatformName"/>): every installed mod with its icon, version, author and description
/// (<see cref="ModManifestAttribute"/>) or why it did not load, an Off/On selector that turns it on or off at the next
/// start (<see cref="InstalledMods"/>), and for a running mod that bound settings, a page of
/// <see cref="SettingControls.For{T}(ConfigEntry{T})"/> controls bound to its <see cref="ModConfig"/>, in each section by
/// <see cref="ConfigEntry{T}.Order"/>. A change applies at once (the mod hears it through
/// <see cref="ConfigEntry{T}.Changed"/>, and the page follows changes the mod makes itself); the file is written when the
/// page closes. A setting that applies only after a restart says so, and the page asks for one once such a setting changed.
/// <para>Laid out like a mod manager: the title names the platform and its version, the list holds only the mods (a
/// turned-off one with its name dimmed). The windows' buttons and their restart note stand in a footer that never
/// scrolls, the platform's own Settings beside Close, and the note keeps its place there while hidden, so neither window
/// changes its size when it shows.</para>
/// </summary>
internal static class ModsWindow
{
    /// <summary>The menu buttons' label and the list window's title: the platform's name, the same in every language.</summary>
    internal const string PlatformName = "Mjolnir";

    private const string Version = "#C6E6DACC"; // the game's value-text mint, dimmed
    private const string VersionOff = "#C6E6DA66"; // the same for a mod that is off
    private const string NameOff = "#80"; // the alpha of a turned-off mod's name
    private const string Warning = "#FFB31CFF"; // the amber of the game's highlights
    private const double FooterButtonWidth = 220;
    private static int s_installed;
    private static UiWindow? s_list;
    private static UiWindow? s_platformPage;
    private static readonly Dictionary<Mod, UiWindow> s_pages = [];

    /// <summary>The width of a mod row's control part: the Off/On selector leaves the description room.</summary>
    internal const double RowContentWidth = 240;

    /// <summary>Adds the two buttons; the windows are built on first open. Called when the first mod attaches.</summary>
    public static void EnsureInstalled()
    {
        if (Interlocked.Exchange(ref s_installed, 1) == 1) return;
        try
        {
            Menus.Main.AddPlatformButton(Button(PlatformName, Open));
            Menus.Pause.AddPlatformButton(Button(PlatformName, Open));
        }
        catch (Exception e)
        {
            SdkLog.Error(GameUi.Source, $"Mods window not installed: {e}");
        }
    }

    private static void Open()
    {
        s_list ??= BuildList();
        s_list.Show();
    }

    private static UiWindow BuildList()
    {
        var mods = InstalledMods.Current;
        string settings = Theme.Loc("common.settings", "Settings");
        var window = new UiWindow
        {
            Title = ListTitle, ObjectName = PlatformName, Width = 1000, SizeToContent = SizeToContent.Height, MaxHeight = 760,
        };
        window.Content = ListContent(mods, settings, (title, text, yes, no) => Dialog.ConfirmAsync(title, text, yes, no));
        window.Footer = ListFooter(mods, Button(settings, OpenPlatformSettings, "PlatformSettings"),
                                   Button(Theme.Loc("common.close", "Close"), window.Close, "Close"));
        return window;
    }

    /// <summary>The platform's version as people write it (0.2.0).</summary>
    internal static string PlatformVersion => typeof(Mod).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "";

    /// <summary>The list window's title: the platform's name and, smaller and dimmed, its version.</summary>
    internal static string ListTitle => $"{PlatformName}  <size=70%><color={Version}>{PlatformVersion}</color></size>";

    // The platform's own settings (PlatformConfig): the interface scale today.
    private static void OpenPlatformSettings()
    {
        s_platformPage ??= BuildSettingsWindow(PlatformName, PlatformConfig.Config, SdkTexts.Strings);
        s_platformPage.Show();
    }

    /// <summary>Asks the player (title, text, the yes and no buttons' texts, null for the game's Cancel); true for yes.</summary>
    internal delegate Task<bool> Confirm(string title, string text, string yes, string? no);

    /// <summary>
    /// The list (no Unity: unit-tested): a row per installed mod, named <c>Mod &lt;id&gt;</c>, with its status line and an
    /// Off/On selector, and <paramref name="settingsText"/> on a button for a running mod with settings. Turning off a mod
    /// that mods which are on cannot load without, or turning on a mod that needs turned-off ones, asks first through
    /// <paramref name="confirm"/>.
    /// </summary>
    /// <param name="mods">The installed mods.</param>
    /// <param name="settingsText">The Settings button's word.</param>
    /// <param name="confirm">Asks the player.</param>
    internal static UiStackPanel ListContent(InstalledMods mods, string settingsText, Confirm confirm)
    {
        var rows = mods.All.ToDictionary(mod => mod, mod => Row(mods, mod, settingsText));
        foreach (var (mod, row) in rows) row.Toggled += (_, _) => _ = Toggle(mods, mod, rows, confirm);
        var content = new UiStackPanel();
        if (mods.All.Count == 0) content.Children.Add(new UiTextBlock { Name = "NoMods", Text = SdkTexts.Get("mods.none") });
        content.Children.Add(mods.All.Select(mod => rows[mod]));
        return content;
    }

    /// <summary>
    /// The list window's footer (no Unity: unit-tested): the note while a change waits for a restart, keeping its place
    /// while hidden, then <paramref name="platformSettings"/> (the platform's own settings) and <paramref name="close"/>.
    /// </summary>
    internal static UiGrid ListFooter(InstalledMods mods, UiButton platformSettings, UiButton close) =>
        Footer(RestartNote(SdkTexts.Get("mods.apply"), () => mods.HasPendingChanges), platformSettings, close);

    /// <summary>
    /// A settings page's footer (no Unity: unit-tested): for a mod with settings that apply after a restart, a note that
    /// shows once one of them differs from its value when the page was made; then <paramref name="reset"/> and
    /// <paramref name="close"/>.
    /// </summary>
    internal static UiGrid SettingsFooter(ModConfig config, UiButton reset, UiButton close)
    {
        var restart = config.Entries.Where(e => e.IsRestartRequired).Select(e => (Entry: e, Start: e.BoxedValue)).ToArray();
        return Footer(RestartNote(SdkTexts.Get("mods.restart"), () => restart.Any(r => !Equals(r.Entry.BoxedValue, r.Start))), reset, close);
    }

    // A footer: the note takes the room the buttons leave, the buttons stand on the right, each in its own column; all
    // in letters of 18, readable on the game's buttons (their own size to fit is tiny at 36 tall).
    private static UiGrid Footer(UiTextBlock note, params UiButton[] buttons)
    {
        var footer = new UiGrid
        {
            ColumnDefinitions = "*" + string.Concat(buttons.Select(_ => FormattableString.Invariant($",{FooterButtonWidth}"))),
            ColumnSpacing = 12,
            FontSize = 18,
            Children = { note },
        };
        foreach (var button in buttons)
        {
            button.Width = FooterButtonWidth;
            footer.Children.Add(button);
        }
        return footer;
    }

    // As tall as the buttons beside it; hidden by its opacity, so its place stays and nothing moves when it shows.
    private static UiTextBlock RestartNote(string text, Func<bool> show)
    {
        var note = new UiTextBlock
        {
            Name = "RestartNote", Text = Amber(text), TextAlignment = TextAlignment.Left, TextWrapping = TextWrapping.Wrap, Height = 36,
        };
        // A few checks a second are plenty for a note.
        note.Bind(Control.OpacityProperty, () => show() ? 1d : 0d, TimeSpan.FromSeconds(0.25));
        return note;
    }

    // A mod's strings (its lang files), for its name, description and settings in the player's language.
    private static ModStrings? StringsOf(Mod mod) => ModStringsRegistry.ForAssembly(mod.GetType().Assembly);

    private static ModStrings? StringsOf(InstalledMod mod) => ModStringsRegistry.ForAssembly(mod.Manifest.Assembly);

    private static string NameOf(Mod mod) => SettingTexts.ModNameOf(mod.Manifest.Name, StringsOf(mod));

    private static string NameOf(InstalledMod mod) => SettingTexts.ModNameOf(mod.Manifest.Name, StringsOf(mod));

    private static ModRow Row(InstalledMods mods, InstalledMod mod, string settingsText)
    {
        var manifest = mod.Manifest;
        var row = new ModRow
        {
            Name = $"Mod {mod.Id}",
            ContentWidth = RowContentWidth,
            Header = HeaderOf(mods, mod),
            Description = StatusOf(mods, mod),
            IsOn = mods.IsEnabled(mod),
        };
        if (mod is { State: InstalledState.Running, Mod: { ConfigIfUsed.Entries.Count: > 0 } running })
        {
            row.SettingsText = settingsText;
            row.SettingsClick += (_, _) => OpenSettings(running);
        }
        ShowIcon(row, manifest.IconPath, manifest.Name, manifest.Id);
        return row;
    }

    /// <summary>A row's name line: the name, then the version and author, dimmed; all of it dimmer for a mod that is off.</summary>
    internal static string HeaderOf(InstalledMods mods, InstalledMod mod)
    {
        var manifest = mod.Manifest;
        string version = manifest.Author is { } author ? $"{manifest.VersionText} · {author}" : manifest.VersionText;
        return mods.IsEnabled(mod)
            ? $"{NameOf(mod)}  <color={Version}>{version}</color>"
            : $"<alpha={NameOff}>{NameOf(mod)}  <color={VersionOff}>{version}</color>";
    }

    /// <summary>
    /// A row's status line: the mod's description while it runs as chosen, else what became of it or will, in amber for
    /// problems and for changes waiting for a restart (no Unity: unit-tested).
    /// </summary>
    internal static string StatusOf(InstalledMods mods, InstalledMod mod)
    {
        bool on = mods.IsEnabled(mod);
        if (mods.IsPending(mod)) return Amber(SdkTexts.Get(on ? "mods.turns_on" : "mods.turns_off"));
        if (!on || mod.State == InstalledState.TurnedOff) return SdkTexts.Get("mods.off");
        return mod.State switch
        {
            InstalledState.NotLoaded => Amber(SdkTexts.Get("mods.not_loaded", ReasonOf(mods, mod.Problem))),
            InstalledState.Failed => Amber(SdkTexts.Get("mods.failed")),
            _ when mod.Manifest.IsDisabled => Amber(SdkTexts.Get("mods.disabled")),
            _ => SettingTexts.ModDescriptionOf(mod.Manifest.Description, StringsOf(mod)) ?? mod.Id,
        };
    }

    /// <summary>Why a mod did not load, in the player's language, naming installed mods by their names.</summary>
    internal static string ReasonOf(InstalledMods mods, InstalledProblem? problem)
    {
        if (problem == null) return "";
        string other = problem.Other == null ? "" : NameOrId(mods, problem.Other);
        string asked = string.IsNullOrEmpty(problem.Range) || problem.Range == "*" ? other : $"{other} {problem.Range}";
        return problem.Kind switch
        {
            InstalledProblemKind.Duplicate => SdkTexts.Get("mods.reason.duplicate", problem.Other),
            InstalledProblemKind.Missing => SdkTexts.Get("mods.reason.missing", asked),
            InstalledProblemKind.WrongVersion => SdkTexts.Get("mods.reason.wrong_version", asked, problem.Detail),
            InstalledProblemKind.DependencyNotLoaded => SdkTexts.Get("mods.reason.dependency_not_loaded", other),
            InstalledProblemKind.DependencyTurnedOff => SdkTexts.Get("mods.reason.dependency_turned_off", other),
            InstalledProblemKind.DependencyFailed => SdkTexts.Get("mods.reason.dependency_failed", other),
            InstalledProblemKind.SelfDependency => SdkTexts.Get("mods.reason.self_dependency"),
            InstalledProblemKind.Cycle => SdkTexts.Get("mods.reason.cycle", problem.Other),
            _ => problem.Kind.ToString(),
        };
    }

    private static string NameOrId(InstalledMods mods, string id) =>
        mods.All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)) is { } mod ? NameOf(mod) : id;

    private static string Amber(string text) => $"<color={Warning}>{text}</color>";

    // The player flipped a row's selector (its IsOn already changed): asks about the mods involved, then writes the choice.
    private static async Task Toggle(InstalledMods mods, InstalledMod mod, IReadOnlyDictionary<InstalledMod, ModRow> rows, Confirm confirm)
    {
        try
        {
            string name = NameOf(mod);
            if (!rows[mod].IsOn)
            {
                var needing = mods.RequiredBy(mod);
                if (needing.Count > 0)
                {
                    string text = SdkTexts.Get(needing.Count == 1 ? "mods.required_by" : "mods.required_by_many", Names(needing), name);
                    if (!await confirm(name, text, SdkTexts.Get("mods.turn_off"), null))
                    {
                        rows[mod].IsOn = true;
                        return;
                    }
                }
                Apply(mod, false);
                return;
            }

            var needed = mods.TurnedOffRequirements(mod);
            Apply(mod, true);
            if (needed.Count == 0) return;
            string ask = SdkTexts.Get(needed.Count == 1 ? "mods.requires" : "mods.requires_many", name, Names(needed));
            if (await confirm(name, ask, SdkTexts.Get("mods.turn_on_too"), SdkTexts.Get("mods.only_this")))
            {
                foreach (var dependency in needed) Apply(dependency, true);
            }
        }
        catch (Exception e)
        {
            SdkLog.Error(GameUi.Source, $"Turning {mod.Id} on or off failed: {e}");
        }

        string Names(IEnumerable<InstalledMod> list) => string.Join(", ", list.Select(NameOf));

        // A choice that could not be written (logged) puts the selector back.
        void Apply(InstalledMod target, bool on)
        {
            mods.SetEnabled(target, on);
            if (!rows.TryGetValue(target, out var row)) return;
            row.IsOn = mods.IsEnabled(target);
            row.Header = HeaderOf(mods, target);
            row.Description = StatusOf(mods, target);
        }
    }

    /// <summary>The mod's picture left of its name, or a tile with the name's first letter (no Unity: unit-tested).</summary>
    internal static void ShowIcon(HeaderedControl row, string? iconPath, string name, string id)
    {
        if (iconPath != null) row.Icon = new Bitmap(iconPath);
        else row.Monogram = Monogram.For(name, id);
    }

    private static void OpenSettings(Mod mod)
    {
        if (!s_pages.TryGetValue(mod, out var page)) s_pages[mod] = page = BuildSettings(mod);
        page.Show();
    }

    private static UiWindow BuildSettings(Mod mod) => BuildSettingsWindow(NameOf(mod), mod.ConfigIfUsed!, StringsOf(mod));

    // A settings page: the config's controls (SettingsContent), and in its footer Reset and Close; the file is written when it closes.
    private static UiWindow BuildSettingsWindow(string name, ModConfig config, ModStrings? strings)
    {
        var window = new UiWindow
        {
            Title = $"{name} · {Theme.Loc("common.settings", "Settings")}", Width = 1100, SizeToContent = SizeToContent.Height, MaxHeight = 760,
        };
        string resetText = Theme.Loc("common.reset", "Reset");
        var reset = Button(resetText, () => Dialog.Confirm(name, SdkTexts.Get("mods.reset"), () =>
        {
            foreach (var entry in config.Entries) entry.BoxedValue = entry.BoxedDefault;
        }, confirmText: resetText, cancelText: Theme.Loc("common.cancel", "Cancel")), "Reset");
        window.Content = SettingsContent(config, strings);
        window.Footer = SettingsFooter(config, reset, Button(Theme.Loc("common.close", "Close"), window.Close, "Close"));
        window.Closed += (_, _) => config.SaveIfChanged();
        return window;
    }

    /// <summary>
    /// A header per section (sections in binding order) and a bound control per setting, by <see cref="ConfigEntry{T}.Order"/>
    /// and then binding order, their texts in the player's language from the mod's <paramref name="strings"/> when they have
    /// them (<see cref="SettingTexts"/>). The restart note is the footer's (<see cref="SettingsFooter"/>). No Unity: unit-tested.
    /// </summary>
    internal static UiStackPanel SettingsContent(ModConfig config, ModStrings? strings = null)
    {
        var content = new UiStackPanel();
        foreach (var section in config.Entries.GroupBy(e => e.Section))
        {
            content.Children.Add(new UiSectionHeader { Text = SettingTexts.SectionOf(section.Key, strings) });
            // A stable sort: binding order breaks ties.
            content.Children.Add(section.OrderBy(e => e.Order).Select(entry => SettingControls.For(entry, strings)));
        }
        return content;
    }

    private static UiButton Button(string content, Action click, string? name = null)
    {
        var button = new UiButton { Content = content, Name = name };
        button.Click += (_, _) => click();
        return button;
    }
}
