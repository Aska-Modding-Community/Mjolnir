using System;
using System.IO;
using System.Runtime.InteropServices;
using Mjolnir.SDK;
using Mjolnir.SDK.Content;
using Mjolnir.SDK.UI;
using SandSailorStudio.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Mjolnir.UiLab;

/// <summary>
/// In-game test of the SDK's modules: mostly Mjolnir.SDK.UI (it grew out of the raw UI research), and
/// Mjolnir.SDK.Player in the "player" scenario (PlayerScenario.cs):
/// <list type="bullet">
/// <item>a stress case on top of examples/Mjolnir.UiExample: a pause button opening two windows in a stack, the top one
/// asking a Dialog.Confirm, so the dialog must draw above the windows and every Back must close exactly one layer;</item>
/// <item>F8 toggles the first window (the hotkey pattern);</item>
/// <item>the cursor as Windows shows it (screenshots never contain the OS cursor);</item>
/// <item>a one-shot autopilot (Autopilot.cs) that plays a scenario, takes screenshots and quits.</item>
/// </list>
/// </summary>
// ServerOnly: the autopilot drives the host alone; a client joining it needs no UiLab (the "nethost" scenario).
[ModManifest("mjolnir.test.uilab", "UiLab", "0.6.0", Network = NetworkCompatibility.ServerOnly)]
public sealed class UiLab : Mod
{
    internal static UiLab Instance = null!;
    internal static UiWindow First = null!;
    internal static UiWindow Second = null!;
    internal static UiTextBlock TaskText = null!;
    internal static int Opened;
    internal static int Closed;

    public UiLab() => Instance = this;

    public override void OnLoad()
    {
        Autopilot.Enabled = Config.Bind("Autopilot", "Enabled", false,
            "Play the UI scenario by itself, take screenshots and quit (also triggered once by a file autopilot.once in the mod folder).").Value;
        Autopilot.SaveName = Config.Bind("Autopilot", "SaveName", "ХзКтоТут", "Save slot to load, by its name in the load list.").Value;
        Autopilot.Multiplayer = Config.Bind("Autopilot", "Multiplayer", true, "Load the save from the multiplayer page, hosting it, not from the single player one.").Value;
        Autopilot.QuitWhenDone = Config.Bind("Autopilot", "QuitWhenDone", true, "Quit the game when the scenario ends.").Value;
        string folder = Path.GetDirectoryName(Manifest.Location)!;
        Autopilot.ShotsDir = Path.Combine(folder, "shots");
        Autopilot.ConfigDir = Path.GetDirectoryName(Config.FilePath)!;
        // One-shot trigger for automated runs: the file is consumed, so the next manual launch plays normally. Its text
        // names the scenario: empty or "ui" for the UI facade test, "research" for the settings-controls and HUD dump,
        // "player" for the Mjolnir.SDK.Player check, "localization" for the research behind the SDK's localization.
        string once = Path.Combine(folder, "autopilot.once");
        if (File.Exists(once))
        {
            string scenario = File.ReadAllText(once).Trim();
            File.Delete(once);
            Autopilot.Enabled = true;
            if (scenario.Length > 0) Autopilot.Scenario = scenario;
        }
        if (Autopilot.Enabled) Log.Info($"Cloud conflict answers: {Harmony.CreateClassProcessor(typeof(CloudConflict)).Patch()?.Count ?? 0} method(s) patched");
        if (Autopilot.Enabled && Autopilot.Scenario == "player")
            Log.Info($"Player probes: {Harmony.CreateClassProcessor(typeof(PlayerProbes)).Patch()?.Count ?? 0} method(s) patched");
        if (Autopilot.Enabled && Autopilot.Scenario == "localization")
            Log.Info($"Localization probes: {Harmony.CreateClassProcessor(typeof(LocalizationProbes)).Patch()?.Count ?? 0} method(s) patched");
        // An effect only the "stats" scenario applies: what a save keeps of a mod's effect.
        if (Autopilot.Enabled && Autopilot.Scenario == "stats")
            Effects.Add("lab_effect").Modifies("Attribute_Dexterity", 2);
        if (Autopilot.Enabled && Autopilot.Scenario == "settlement")
            Log.Info($"Settlement probes: {Harmony.CreateClassProcessor(typeof(SettlementProbes)).Patch()?.Count ?? 0} method(s) patched");
        if (Autopilot.Enabled && Autopilot.Scenario == "villagers")
        {
            Log.Info($"Villager probes: {Harmony.CreateClassProcessor(typeof(VillagerProbes)).Patch()?.Count ?? 0} method(s) patched");
            // A job that asks more than the example's greeting while the scenario wants it: it must interrupt it.
            Mjolnir.SDK.AI.Residents.AddJob("lab_pause", v => ReferenceEquals(v, Autopilot.LabPauseFor) ? 30f : -1f)
                .Wait(System.TimeSpan.FromSeconds(4))
                .Do(v => Autopilot.LabPauseFor = null);
            // A job that asks more than work, for one worker: it must leave its work for the job and go back to it.
            Mjolnir.SDK.AI.Residents.AddJob("lab_worker", v => ReferenceEquals(v, Autopilot.LabWorker) ? 20f : -1f)
                .Wait(System.TimeSpan.FromSeconds(3))
                .Do(v => Autopilot.LabWorker = null);
        }
        // A job that plays an animation (by name: the game's clips are not known yet) for the villager the scenario names.
        if (Autopilot.Enabled && Autopilot.Scenario == "animation")
            Mjolnir.SDK.AI.Residents.AddJob("lab_animation", v => ReferenceEquals(v, Autopilot.LabAnimated) ? 30f : -1f)
                .Do(v => Autopilot.LabAnimationStarted = UnityEngine.Time.realtimeSinceStartup)
                .Play("aska_wave", new Mjolnir.SDK.AnimationOptions { Loop = true }, System.TimeSpan.FromSeconds(4))
                .Do(v => Autopilot.LabAnimated = null);
        // A walk the scenario sends a villager on, to wave at it on the way.
        if (Autopilot.Enabled && Autopilot.Scenario == "animation")
            Mjolnir.SDK.AI.Residents.AddJob("lab_walk", v => ReferenceEquals(v, Autopilot.LabWalker) ? 30f : -1f)
                .MoveTo(v => Autopilot.LabWalkTo, 1.5f)
                .Do(v => Autopilot.LabWalker = null);
        // A clip made in Blender, for the "blender" scenario (its bundle is made from the game's skeleton, not kept in git).
        if (Autopilot.Enabled && Autopilot.Scenario == "blender")
            Autopilot.BlenderRaise = Mjolnir.SDK.Animation.AnimationsExtensions.Add("blender_raise", "bundles/blender", "BlenderRaise");
        // Two clips of the kit's for the "partial" scenario: the bones a clip has no curves for.
        if (Autopilot.Enabled && Autopilot.Scenario == "partial")
        {
            Autopilot.ArmOnly = Mjolnir.SDK.Animation.AnimationsExtensions.Add("arm_only", "bundles/partial", "ArmOnly");
            Autopilot.ArmAndRest = Mjolnir.SDK.Animation.AnimationsExtensions.Add("arm_and_rest", "bundles/partial", "ArmAndRest");
            // A gesture made in Blender (only the right arm moves; the kit left the rest to the game) and the kit's mask from it.
            Autopilot.BlenderWave = Mjolnir.SDK.Animation.AnimationsExtensions.Add("blender_wave", "bundles/blender", "BlenderWave");
            Autopilot.BlenderWaveMask = Mjolnir.SDK.Animation.AnimationMaskExtensions.Add("blender_wave_mask", "bundles/blender", "BlenderWaveMask");
        }
        // Mixamo's clips baked onto the game's skeleton by the kit, for the "mixamo" scenario (its bundle is not kept in git).
        if (Autopilot.Enabled && Autopilot.Scenario == "mixamo")
            foreach (string name in Autopilot.MixamoNames)
                Autopilot.MixamoClips.Add(Mjolnir.SDK.Animation.AnimationsExtensions.Add(name.Replace(' ', '_').ToLowerInvariant(), "bundles/mixamo", name));
        // A building of several construction stages with a cost of its own, for the "buildings" scenario.
        if (Autopilot.Enabled && Autopilot.Scenario is "buildings" or "cart research")
            Mjolnir.SDK.Content.Buildings.Add("lab_hut", basedOn: "Item_Structures_BuilderHutL1").Needs("Item_Wood_Sticks", 3);
        // The same with models of its stages (AltarExample's altar from its folder: stage 1 only a construction look), for the
        // "stages" scenario. One bundle: two mods' bundles of one name (both examples' are 'models') do not both load.
        if (Autopilot.Enabled && Autopilot.Scenario == "stages")
            Mjolnir.SDK.Content.Buildings.Add("lab_hut", basedOn: "Item_Structures_BuilderHutL1").Needs("Item_Wood_Sticks", 3)
                .WithModel("../Mjolnir.AltarExample/bundles/models", "Altar", stage: 0)
                .WithConstructionModel("../Mjolnir.AltarExample/bundles/models", "Altar", stage: 1)
                .WithModel("../Mjolnir.AltarExample/bundles/models", "Altar", stage: 2);
        // Items of a kind with what makes them the mod's, for the "equipment" scenario.
        if (Autopilot.Enabled && Autopilot.Scenario == "equipment")
        {
            Mjolnir.SDK.Content.Items.Add("lab_sword", Mjolnir.SDK.Content.ItemKind.OneHandedWeapon)
                .WithDamage(30, 40).AppliesOnHit("Effect_Burning", TimeSpan.FromSeconds(4));
            Mjolnir.SDK.Content.Items.Add("lab_hood", Mjolnir.SDK.Content.ItemKind.Helmet)
                .WithArmor(500).WithAttribute("Attribute_ColdResistance", 7).AppliesWhileEquipped("Effect_Energized");
            Mjolnir.SDK.Content.Items.Add("lab_cake", Mjolnir.SDK.Content.ItemKind.Food)
                .AppliesWhenEaten("Effect_Energized", TimeSpan.FromMinutes(1));
        }
        // A mod building on the game's cart, no model of its own, for the "cart research" scenario: is the cart's function its?
        if (Autopilot.Enabled && Autopilot.Scenario == "cart research")
            Mjolnir.SDK.Content.Buildings.Add("lab_cart", basedOn: "Item_Bushcraft_Cart");

        First = new UiWindow { Title = "UiLab 1", Width = 700, Height = 360 };
        TaskText = new UiTextBlock { Header = "From a task", Name = "TaskText", Text = "not set yet" };
        First.Content = new UiStackPanel
        {
            Children = { new UiTextBlock { Text = "The first window. The second one opens on top of it." }, TaskText, DisabledPanel(), Button("Close", First.Close) },
        };
        Second = new UiWindow { Title = "UiLab 2", Width = 620, Height = 320, WindowStyle = WindowStyle.Dialog }; // the dialog look beside the main one
        Second.Content = new UiStackPanel
        {
            Children =
            {
                new UiTextBlock { Text = "The second window, on top of the first.\nAsk opens the game's dialog above both." },
                Button("Ask", () => Dialog.Confirm("UiLab", "A dialog over two mod windows.",
                                                   () => Log.Info("Dialog: confirmed"), () => Log.Info("Dialog: cancelled"))),
                Button("Close", Second.Close),
            },
        };
        foreach (var window in new[] { First, Second })
        {
            var w = window;
            w.Opened += (_, _) => Log.Info($"{w.Title} opened ({++Opened} opened events); {CursorProbe.Describe()}");
            w.Closed += (_, _) => Log.Info($"{w.Title} closed ({++Closed} closed events)");
        }
        Menus.Pause.Add(Button("UiLab: two windows", () =>
        {
            First.Show();
            Second.Show();
        }));
        Menus.Main.Opened += (_, _) => Autopilot.MainMenuReady = true;
        Hud.Ready += (_, _) => Autopilot.HudReady = true;
        Log.Info($"Autopilot {(Autopilot.Enabled ? $"on: scenario '{Autopilot.Scenario}', save '{Autopilot.SaveName}', quit when done {Autopilot.QuitWhenDone}" : "off")}");
    }

    public override void OnGameReady()
    {
        if (Autopilot.Enabled) Autopilot.Start();
    }

    public override void OnUpdate()
    {
        Autopilot.Update();
        if (Keyboard.current?.f8Key.wasPressedThisFrame != true) return;
        if (First.IsOpen) First.Close();
        else First.Show();
    }

    // A disabled panel with an enabled and a disabled button in it: both dimmed once by the panel, not the disabled one twice.
    private static UiStackPanel DisabledPanel() => new()
    {
        Name = "LabDisabledPanel", IsEnabled = false, Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center,
        Children =
        {
            new UiButton { Name = "LabInnerEnabled", Content = "Inner, enabled", Width = 220 },
            new UiButton { Name = "LabInnerDisabled", Content = "Inner, disabled", Width = 220, IsEnabled = false },
        },
    };

    private static UiButton Button(string content, Action click)
    {
        var button = new UiButton { Content = content };
        button.Click += (_, _) => click();
        return button;
    }
}

/// <summary>Unity's cursor state next to what Windows actually shows.</summary>
internal static class CursorProbe
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo
    {
        public int Size;
        public int Flags;
        public IntPtr Handle;
        public Point Position;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorInfo(ref CursorInfo info);

    public static string Describe()
    {
        var info = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        string os = GetCursorInfo(ref info)
            ? $"Windows showing={(info.Flags & 1) != 0}, handle=0x{info.Handle.ToInt64():X}"
            : "Windows ?";
        return $"cursor: unity visible={Cursor.visible}, lock={Cursor.lockState}; {os}; input mode {MenuManager.CurrentInputMode}";
    }
}
