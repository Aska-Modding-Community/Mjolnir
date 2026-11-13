using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mjolnir.SDK;
using Mjolnir.SDK.Net;
using SSSGame;
using UnityEngine;

namespace Mjolnir.UiLab;

/// <summary>
/// Scenario "net": what Mjolnir.SDK.Net does on this machine alone. In the main menu: the refusal a client would see, built
/// from a sample comparison and shown in the game's dialog (a screenshot), and no session. Then the save is hosted: the
/// session's properties carry Mjolnir, the host is player 0 and passes its own check, and a channel answers that it has no
/// one to send to. A client's side needs a second machine.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> NetScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 3f;
        Log($"Net in the main menu: multiplayer {Network.IsMultiplayer} (False expected), players {Network.Players.Count}");

        ShowSampleRefusal();
        yield return 1.5f;
        Shot("net-refusal");
        yield return AfterShot;
        DumpDialog();
        var dialog = SandSailorStudio.UI.Menu.GetInstance<SSSGame.UI.ConfirmActionMenu>();
        var panel = dialog?.MessageRect?.parent?.TryCast<RectTransform>();
        Log($"Net dialog fitted: panel {panel?.sizeDelta}, title {dialog?.TitleRect?.sizeDelta}");
        SandSailorStudio.UI.MenuManager.Instance.BackPressTopMenu();
        yield return 1f;
        Log($"Net dialog closed: destroyed {panel == null} (the game makes a new dialog for each question)");

        foreach (float wait in LoadSave(SandSailorStudio.UI.Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 5f;
        var channel = Network.Channel("lab");
        Log($"Net hosting: multiplayer {Network.IsMultiplayer}, host {Network.IsHost}, local {Network.LocalPlayer}, " +
            $"players [{string.Join(", ", Network.Players)}], a message to all reached {channel.SendToAll([1, 2, 3])} player(s) (0 expected alone)");
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    /// <summary>
    /// Scenario "nethost": hosts the save and stays, for a client on a second machine; every change of the session's
    /// checked players is logged as it comes. It ends when a file nethost.stop appears next to UiLab (or after two hours).
    /// </summary>
    private static IEnumerator<float> NetHostScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 3f;
        foreach (float wait in LoadSave(SandSailorStudio.UI.Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 3f;
        Network.PlayerJoined += (_, e) => Log($"Net host: {e.Player} passed the check; players [{string.Join(", ", Network.Players)}]");
        Network.PlayerLeft += (_, e) => Log($"Net host: {e.Player} left; players [{string.Join(", ", Network.Players)}]");
        Log($"Net host ready: multiplayer {Network.IsMultiplayer}, host {Network.IsHost}, players [{string.Join(", ", Network.Players)}]");
        string stop = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(ShotsDir)!, "nethost.stop");
        var started = DateTime.UtcNow;
        while (!System.IO.File.Exists(stop) && DateTime.UtcNow - started < TimeSpan.FromHours(2)) yield return 1f;
        if (System.IO.File.Exists(stop)) System.IO.File.Delete(stop);
        Log($"Autopilot done: hosted for {(DateTime.UtcNow - started).TotalMinutes:0} min");
        if (QuitWhenDone) Application.Quit();
    }

    // The open dialog's objects with their rectangles and layout components, for fitting a long message.
    private static void DumpDialog()
    {
        var menu = SandSailorStudio.UI.Menu.GetInstance<SSSGame.UI.ConfirmActionMenu>();
        if (menu == null)
        {
            Log("Net dialog: none open");
            return;
        }
        Log($"Net dialog: MessageRect {menu.MessageRect?.name}, TitleRect {menu.TitleRect?.name}, ConfirmButton {menu.ConfirmButton?.name}");
        foreach (var t in menu.GetComponentsInChildren<RectTransform>(true))
        {
            string path = t.name;
            for (var p = t.parent; p != null && p != menu.transform.parent; p = p.parent) path = p.name + "/" + path;
            var parts = t.GetComponents<Component>().Select(c => c.GetIl2CppType().Name).Where(n => n != "RectTransform");
            var text = t.GetComponent<TMPro.TMP_Text>();
            Log($"Net dialog part {path}: active {t.gameObject.activeSelf}, anchors {t.anchorMin}-{t.anchorMax}, pivot {t.pivot}, pos {t.anchoredPosition}, " +
                $"size {t.sizeDelta}, rect {t.rect.width:0}x{t.rect.height:0} [{string.Join(", ", parts)}]" +
                (text != null ? $" text {text.fontSize:0.#} pref {text.preferredHeight:0} wrap {text.textWrappingMode} overflow {text.overflowMode} auto {text.enableAutoSizing}" : ""));
        }
    }

    // The module's refusal for a client that lacks one of the host's mods, has one the host does not, and another version
    // of a third, built through the module's internals as a refused client's session would build it.
    private static void ShowSampleRefusal()
    {
        var net = typeof(Network).Assembly;
        Type stamp = net.GetType("Mjolnir.SDK.Net.ModStamp", true)!;
        Type report = net.GetType("Mjolnir.SDK.Net.CompatibilityReport", true)!;
        Array List(params (string Id, string Name, string Version)[] mods)
        {
            var array = Array.CreateInstance(stamp, mods.Length);
            for (int i = 0; i < mods.Length; i++)
                array.SetValue(Activator.CreateInstance(stamp, mods[i].Id, mods[i].Name, mods[i].Version, NetworkCompatibility.Everyone), i);
            return array;
        }
        var host = List(("mjolnir.itemsexample", "Items Example", "1.0.0"), ("mjolnir.statsexample", "Stats Example", "1.2.0"));
        var client = List(("mjolnir.statsexample", "Stats Example", "1.1.0"), ("mjolnir.cheatinventory", "Cheat Inventory", "1.0.0"));
        object sample = report.GetMethod("Compare", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, ["0.3.0", host, "0.3.0", client])!;
        Log($"Net sample refusal: {report.GetProperty("Summary")!.GetValue(sample)}");
        net.GetType("Mjolnir.SDK.Net.GameNetwork", true)!
           .GetMethod("ShowRefusal", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [sample]);
    }
}
