using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime;
using Mjolnir.SDK.Player;
using SSSGame;
using SSSGame.Render;
using SandSailorStudio;
using SandSailorStudio.Inventory;
using SandSailorStudio.UI;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "structure research" scenario: an interaction of our own on a real structure of the game, as the game's houses carry
/// «Show radius» (a SimpleInteraction in their StructureInteractionArea). A flimsy arch is built in front of the camera
/// (Settlement.CreateStructure + FinishBuild); the player's area, its pickers, touch and outline are logged, a SimpleInteraction
/// is added to its area, the HUD's widget users are listed, it is used, then the player steps aside (is our line left in the
/// HUD's list?) and back, and the arch is dismantled. It loads the save: back it up.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> StructureInteractionResearch()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 8f;
        KeepDaytime();
        yield return 3f;
        var player = GameManagers.Get<PlayerManager>()?.LocalPlayer;
        var agent = player?.playerInteractionAgent?.TryCast<PlayerInteractionAgent>();
        var db = GameManagers.Get<ItemDatabaseManager>()?.Database;
        var settlement = GameManagers.Get<SettlementManager>()?.worldSettlement;
        var arch = db?.GetItemInfo("Item_Decoration_ArchFlimsy")?.TryCast<StructureTemplate>();
        if (agent == null || settlement == null || arch == null)
        {
            Log($"Structure: missing: agent {agent != null}, settlement {settlement != null}, arch {arch != null}");
            yield break;
        }
        var pa = agent.Cast<IInteractionAgent>();
        var widget = Object.FindObjectsByType<InteractWidget>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).FirstOrDefault();

        // The arch in front of the camera, on the ground.
        var cam = Camera.main!.transform;
        var forward = cam.forward;
        forward.y = 0;
        forward.Normalize();
        var at = cam.position + forward * 3.5f;
        at = GroundPoint(at) ?? at;
        var placement = new PointPlacement();
        placement.position = at;
        placement.rotation = Quaternion.LookRotation(Vector3.Cross(Vector3.up, forward), Vector3.up); // across the view
        Structure? built = null;
        try
        {
            built = settlement.CreateStructure(arch, placement, null, StructureTemplate.Options.None, 0);
            built?.buildSite?.FinishBuild(-1);
        }
        catch (Exception e)
        {
            Log($"Structure: building failed: {e}");
        }
        if (built == null) yield break;
        yield return 3f;
        var area = built.GetComponentInChildren<StructureInteractionArea>(true);
        Log($"Structure: '{built.name}' at {Vector3.Distance(at, cam.position):0.0} m; areas {built.GetComponentsInChildren<StructureInteractionArea>(true).Length}, the area '{PathOf(area?.transform)}' interactions {area?.interactions?.Count} " +
            $"[{string.Join(", ", area?.interactions?.ToArray().Select(i => $"{i.GetIl2CppType().Name}/{i.order}/{i.name}") ?? [])}], pickables {area?.pickables?.Count}, interactable on it {area?.GetComponent<SSSGame.Network.NetworkObjectInteractable>() != null}");
        ReportArea("before ours", agent, pa, area, widget);
        Shot("structure-before");

        // Ours in its area, as a house carries «Show radius».
        var holder = new GameObject("UiLab_Pray");
        holder.SetActive(false);
        holder.transform.SetParent(area!.transform, false);
        holder.layer = area.gameObject.layer;
        var ours = holder.AddComponent<SimpleInteraction>();
        var template = Resources.FindObjectsOfTypeAll<SimpleInteraction>().FirstOrDefault(s => s.interactSlot != null);
        ours.host = area.gameObject;
        ours.order = InteractionOrder.SECONDARY;
        ours.mode = template?.mode ?? InteractionMode.CONTINUOUS;
        ours.accessDistance = 3f;
        ours.holdTargetTime = 1.5f;
        ours.interactSlot = template?.interactSlot;
        ours.widgetIcon = template?.widgetIcon;
        ours.widgetMessage = "UiLab pray";
        ours.interactable = template != null ? template.interactable : null; // the house's is its structure's own; ours none
        ours.interactable = null;
        ours.events ??= new Interaction.InteractionEvents();
        ours.events.OnBegin ??= new UnityEvent();
        ours.events.OnEnd ??= new UnityEvent();
        ours.events.OnFinish ??= new UnityEvent();
        ours.events.OnFinish.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(new Action(() => Log("Structure: event OnFinish"))));
        holder.SetActive(true);
        area.interactions.Add(ours);
        area.SetAreaDirty();
        yield return 2f;
        ReportArea("with ours", agent, pa, area, widget);
        Shot("structure-ours");
        Log($"Structure: ours touched {ours.IsTouchedByAgent(pa)}, touchy {ours._touchyAgentsTable?.Count}; Use {ours.Use(pa)}");
        yield return 1f;
        Log($"Structure: after the use: a session {ours.FindActiveSession(pa)?.SessionState.ToString() ?? "none"}, locked {agent.LockActiveInteraction}");

        // The player steps aside, then back: is our line left in the HUD?
        var start = LocalPlayer.Position;
        var side = Vector3.Cross(Vector3.up, forward);
        LocalPlayer.Teleport(start + side * 8f);
        yield return 2.5f;
        ReportArea("aside", agent, pa, area, widget);
        Shot("structure-aside");
        LocalPlayer.Teleport(start);
        yield return 2.5f;
        ReportArea("back", agent, pa, area, widget);
        LocalPlayer.Teleport(start + side * 8f);
        yield return 2.5f;
        ReportArea("aside again", agent, pa, area, widget);
        LocalPlayer.Teleport(start);
        yield return 1.5f;

        area.interactions.Remove(ours);
        area.SetAreaDirty();
        Object.Destroy(holder);
        yield return 0.5f;
        try
        {
            built.Dismantle(true);
        }
        catch (Exception e)
        {
            Log($"Structure: dismantling failed: {e.Message}");
        }
        yield return 1f;
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    private static void ReportArea(string when, PlayerInteractionAgent agent, IInteractionAgent pa, StructureInteractionArea? area, InteractWidget? widget)
    {
        var active = agent.GetActiveInteractionArea();
        var users = new List<string>();
        var list = widget?._activeUsersList;
        for (int i = 0; list != null && i < list.Count; i++)
        {
            var user = list[i];
            var obj = user?.TryCast<Il2CppSystem.Object>();
            string kind = obj?.GetIl2CppType().Name ?? "?";
            var session = user?.TryCast<InteractionSession>();
            users.Add(session != null ? $"{kind}@{session.Interaction?.name}" : kind);
        }
        Log($"Structure: {when}: the ray on {(active != null ? $"'{active.GetGameObject()?.name}'" : "nothing")}, " +
            $"area pickers {area?.PickersCount} requests {area?.RequestsCount} touched {(area != null && area.IsTouchedByAgent(pa))}, " +
            $"the HUD's users {users.Count}: [{string.Join(", ", users)}]");
    }
}
