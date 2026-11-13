using System;
using Mjolnir.SDK;
using Mjolnir.SDK.UI;
using Mjolnir.UiExample.ViewModels;
using UnityEngine;
using UnityEngine.UI;

namespace Mjolnir.UiExample.Views;

/// <summary>
/// A window of its own: derive from UiWindow, build the elements in the constructor and set Content. It shows every
/// element of the SDK, a section each; a section creates its elements, says what they do and puts them together. Its
/// state lives in <see cref="ExampleViewModel"/>. Its section headers, the intro, the messages and the difficulty names
/// are in the player's language (lang/*.toml, the SDK's Localization). It makes a texture, which needs the running game, so the mod builds it on
/// first use.
/// </summary>
internal sealed class ExampleWindow : UiWindow
{
    private static readonly string[] Seasons = ["Spring", "Summer", "Autumn", "Winter", "Long winter", "Endless night"];

    private readonly ExampleViewModel _vm = new();
    private readonly ExampleSettings _settings;
    private readonly ModLogger _log;
    private UiTextBox _yourName = null!;
    private UiSlider _volume = null!;

    public ExampleWindow(ExampleSettings settings, ModLogger log)
    {
        _settings = settings;
        _log = log;
        Title = "UI Example";
        Width = 900;
        Height = 640;
        Closed += (s, e) => _log.Info($"Window closed after {_vm.Clicks} click(s)");

        // One column; a section's elements go into it as they are (Children takes a sequence too).
        Content = new UiStackPanel
        {
            Children =
            {
                Intro(),
                FormSection(),
                ControlsSection(),
                LayoutSection(),
                MoreSection(),
                SizesSection(),
                MessagesSection(),
            },
        };
    }

    private enum Difficulty
    {
        Easy,
        Normal,
        Hard,
    }

    private static UiTextBlock Intro()
    {
        var intro = new UiTextBlock();
        intro.Bind(UiTextBlock.TextProperty, Localization.GetBindable("intro"));
        return intro;
    }

    // A header in the player's language: the key's text in lang/<language>.toml, English where a language has none.
    private static UiSectionHeader Section(string key)
    {
        var header = new UiSectionHeader();
        header.Bind(UiSectionHeader.TextProperty, Localization.GetBindable(key));
        return header;
    }

    // A name in a text field, and a button that greets with it.
    private Control[] FormSection()
    {
        var yourName = _yourName = new UiTextBox { Header = "Your name", Text = "Ragnar", MaxLength = 20 };
        var hello = new UiButton { Content = "Say hello" };

        hello.Click += (s, e) => hello.Content = $"Hello, {yourName.Text}!";

        return [Section("section.form"), yourName, hello];
    }

    // The view model's property and commands, the config entries the Mods window shows too, and every value control.
    private Control[] ControlsSection()
    {
        var clicks = new UiTextBlock { Header = "Clicks", Description = "A view model's property, bound" };
        clicks.Bind(UiTextBlock.TextProperty, _vm, x => x.Clicks);
        var countClicks = new UiButton
        {
            Header = "Count clicks", Description = "A button row running a command", Content = "Click me", Command = _vm.CountCommand,
        };
        var resetClicks = new UiButton { Name = "ResetClicks", Content = "Reset clicks", Command = _vm.ResetCommand }; // dimmed while there is nothing to reset

        var showHud = new UiToggleSwitch { Header = "Show the HUD line", Description = "Bound to the config, like the Mods window" };
        showHud.Bind(UiToggleSwitch.IsCheckedProperty, _settings.ShowHudLine);
        var volume = _volume = new UiSlider { Header = "Volume", Maximum = 1, Value = 0.8 };
        // The game's health bar look; it follows another element's value as that moves (GetObservable).
        var volumeBar = new UiProgressBar { Name = "ExampleProgress", Maximum = 1, ShowProgressText = true };
        volumeBar.Bind(UiProgressBar.ValueProperty, volume.GetObservable(UiSlider.ValueProperty));
        var villagers = new UiSlider { Header = "Villagers", Maximum = 20, IsSnapToTickEnabled = true, TickFrequency = 1, Value = 4 };
        var difficulty = new UiStepper
        {
            // An enum's items show its names, or [Difficulty] Easy = "…" from the player's lang file.
            Header = "Difficulty", Description = "An enum: its names are the items", ItemsSource = Enum.GetValues<Difficulty>(), SelectedItem = Difficulty.Normal,
        };
        var season = new UiComboBox { Header = "Season", Description = "Many items", ItemsSource = Seasons, SelectedIndex = 0 };
        var hudText = new UiTextBox { Header = "HUD text", Description = "Enter or leaving the field applies it", MaxLength = 40 };
        hudText.Bind(UiTextBox.TextProperty, _settings.HudText);
        var bannerSeconds = SettingControls.For(_settings.BannerSeconds);

        // Subscribed after the values above were set: value events fire for code's changes too.
        volume.ValueChanged += (s, e) => _log.Info($"Volume {e.NewValue:0.##}");
        villagers.ValueChanged += (s, e) => _log.Info($"Villagers {e.NewValue}");
        difficulty.SelectionChanged += (s, e) => _log.Info($"Difficulty {difficulty.SelectedItem}");
        season.SelectionChanged += (s, e) => _log.Info($"Season {season.SelectedItem}");

        return
        [
            Section("section.controls"),
            clicks, countClicks, resetClicks, showHud, volume, volumeBar, villagers, difficulty, season, hudText, bannerSeconds,
        ];
    }

    // A list that follows the view model's ObservableCollection, pictures in a row, and a text placed by its margin.
    private Control[] LayoutSection()
    {
        var addNote = new UiButton { Name = "AddNote", Content = "Add a note", Command = _vm.AddNoteCommand };
        var removeNote = new UiButton { Name = "RemoveNote", Content = "Remove a note", Command = _vm.RemoveNoteCommand };
        var notes = new UiItemsControl
        {
            Name = "Notes",
            ItemsSource = _vm.Notes,
            ItemTemplate = new FuncDataTemplate<string>(note => new UiTextBlock { Header = "Note", Text = note }),
        };
        var picture = new UiImage { Name = "ExampleImage", Source = Checkerboard(), Width = 64, Height = 64 };
        var stripe = new UiUnityHost(Stripe) { Name = "ExampleCustom", Width = 240, Height = 64 };
        var aside = new UiTextBlock
        {
            Name = "ExampleAside", Text = "On the left, 24 px in", FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(24, 0, 0, 0),
        };

        var noteButtons = new UiStackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Children = { addNote, removeNote },
        };
        var pictures = new UiStackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 20, Children = { picture, stripe },
        };
        return [Section("section.layout"), noteButtons, notes, pictures, aside];
    }

    // Newer controls, built in the fluent style (each element one expression): an expander holding a grid, a list
    // scrolling in a frame of its own, a ring following the volume, and a button asking for a name in the game's text
    // dialog, awaited.
    private Control[] MoreSection()
    {
        var details = new UiExpander
        {
            Name = "ExampleExpander",
            Header = Localization.Get("expander.details"),
            Content = new UiGrid
            {
                Name = "ExampleGrid",
                ColumnDefinitions = "220,*",
                Children =
                {
                    new UiTextBlock { Text = Localization.Get("grid.language") },
                    new UiTextBlock().WithBinding(UiTextBlock.TextProperty, () => Localization.Language, TimeSpan.FromSeconds(1)),
                    new UiTextBlock { Text = Localization.Get("grid.clicks") },
                    new UiTextBlock().WithBinding(UiTextBlock.TextProperty, _vm, x => x.Clicks),
                    new UiTextBlock { Text = Localization.Get("grid.volume") },
                    new UiTextBlock().WithBinding(UiTextBlock.TextProperty, _volume.GetObservable(UiSlider.ValueProperty), v => $"{v:0.00}"),
                },
            },
        };

        var lines = new UiStackPanel { Spacing = 4 };
        for (int i = 1; i <= 12; i++) lines.Children.Add(new UiTextBlock { Text = Localization.Get("scroll.line", i) });
        var list = new UiScrollViewer { Name = "ExampleScroll", Height = 160, Content = lines };

        var ring = new UiProgressRing
        {
            Name = "ExampleRing", Maximum = 1, ShowProgressText = true, Foreground = new Color(0.26f, 0.56f, 0.9f),
            ProgressTextForeground = new Color(0.62f, 0.8f, 1f),
        }.WithBinding(UiProgressRing.ValueProperty, _volume.GetObservable(UiSlider.ValueProperty));
        // Width is a ring's diameter: the ring, its icon and its text scale together.
        var small = new UiProgressRing
        {
            Name = "ExampleSmallRing", Width = 60, Maximum = 1, ShowProgressText = true, ProgressTextFormat = "{0:0.0}",
            Foreground = new Color(1f, 0.7f, 0.11f), ProgressTextForeground = new Color(1f, 0.7f, 0.11f),
        }.WithBinding(UiProgressRing.ValueProperty, _volume.GetObservable(UiSlider.ValueProperty));
        // Awaited on the main thread, the rest runs there too once the dialog closed; null means cancelled.
        var rename = new UiButton { Name = "Rename", Content = Localization.Get("button.rename"), Width = 260 }.OnClick(async () =>
        {
            string? name = await Dialog.PromptAsync("UI Example", Localization.Get("message.rename"), _yourName.Text, maxLength: 20);
            if (name != null) _yourName.Text = name;
        });
        var row = new UiStackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 40, Children = { ring, small, rename },
        };
        return [Section("section.more"), details, list, row];
    }

    // Sizes, element by element: a row whose control is narrow (its description gets the room), a panel
    // whose texts take its smaller FontSize, a button in bigger letters with room around them (Padding), rows drawn at
    // 80 % by a UiLayoutTransformControl, a faded line (Opacity), and a paragraph that grows only up to 600 wide.
    private static Control[] SizesSection()
    {
        var narrow = new UiToggleSwitch { Name = "ExampleNarrowRow", ContentWidth = 220, IsChecked = true }
            .WithBinding(HeaderedControl.HeaderProperty, Localization.GetBindable("sizes.narrow"))
            .WithBinding(HeaderedControl.DescriptionProperty, Localization.GetBindable("sizes.narrow_hint"));
        var small = new UiStackPanel
        {
            Name = "ExampleSmallPanel", FontSize = 15,
            Children =
            {
                new UiTextBlock { Name = "ExampleSmallHeader", Text = "FontSize = 15" }.WithBinding(HeaderedControl.HeaderProperty, Localization.GetBindable("sizes.small")),
                new UiToggleSwitch { Name = "ExampleSmallToggle" }.WithBinding(HeaderedControl.HeaderProperty, Localization.GetBindable("sizes.inherited")),
            },
        };
        var big = new UiButton { Name = "ExampleBigButton", FontSize = 22, Width = 360, Height = 44, Padding = new Thickness(16, 0) }
            .WithBinding(UiButton.ContentProperty, Localization.GetBindable("sizes.big"));
        var scaled = new UiLayoutTransformControl
        {
            Name = "ExampleScaled", Scale = 0.8,
            Child = new UiStackPanel
            {
                Children =
                {
                    new UiSlider { Name = "ExampleScaledSlider", Maximum = 10, Value = 4 }.WithBinding(HeaderedControl.HeaderProperty, Localization.GetBindable("sizes.scaled")),
                    new UiButton { Name = "ExampleScaledButton", Content = "0.8 ×" },
                },
            },
        };
        var faded = new UiTextBlock { Name = "ExampleFaded", Opacity = 0.5 }.WithBinding(UiTextBlock.TextProperty, Localization.GetBindable("sizes.faded"));
        var paragraph = new UiTextBlock { Name = "ExampleMaxWidth", MaxWidth = 600, TextAlignment = TextAlignment.Center }
            .WithBinding(UiTextBlock.TextProperty, Localization.GetBindable("sizes.paragraph"));
        return [Section("section.sizes"), narrow, small, big, scaled, faded, paragraph];
    }

    // The game's notifications (one with a picture of ours, one that counts) and dialog, and closing the window.
    private Control[] MessagesSection()
    {
        var notify = new UiButton { Content = "Notify", Width = 190 };
        var count = new UiButton { Name = "ExampleCount", Content = "+3", Width = 190 };
        var ask = new UiButton { Content = "Ask", Width = 190 };
        var close = new UiButton { Content = "Close", Width = 190 };

        // The mod's own strings by their keys, and the game's (common.yes, common.no) the same way. The answer is awaited.
        notify.Click += (s, e) => Notify.Message(Localization.Get("message.hello"), Icons.Diamond);
        // Clicks in quick succession show as one notification counting up, as the game's pickups do.
        count.Click += (s, e) => Notify.Counter("berries", Localization.Get("message.berries"), 3, Icons.Circle);
        ask.Click += async (s, e) =>
            Notify.Message(Localization.Get(await Dialog.ConfirmAsync("UI Example", Localization.Get("message.question")) ? "common.yes" : "common.no"));
        close.Click += (s, e) => Close();

        var buttons = new UiStackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Children = { notify, count, ask, close },
        };
        return [Section("section.messages"), buttons];
    }

    // A texture made in code: UiImage takes any Texture2D or Sprite (or new Bitmap(path) for a PNG next to the mod).
    private static Texture2D Checkerboard()
    {
        var texture = new Texture2D(8, 8) { filterMode = FilterMode.Point };
        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 8; y++)
                texture.SetPixel(x, y, (x + y) % 2 == 0 ? new Color(0.78f, 0.9f, 0.85f) : new Color(1f, 0.7f, 0.11f));
        }
        texture.Apply();
        return texture;
    }

    // Raw Unity UI inside the layout: a translucent amber bar made from a plain UnityEngine.UI.Image.
    private static void Stripe(RectTransform rect)
    {
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(1f, 0.7f, 0.11f, 0.35f);
    }
}
