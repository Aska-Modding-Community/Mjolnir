using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
/// The "structures research" scenario, for a mod's altar made as the game makes its well: (A) every structure template with
/// its footprint, interaction area, build parts and what it does (shots/structures-small.txt), to pick the altar's base;
/// (B) the well's prefab: its build parts' geometry and completed objects, its pickable's objects, its area's description;
/// (C) an object of our own whose area is made as the well's (a trigger with an InteractionAreaLink, a Pickable over its
/// renderers, a StandaloneAreaDescription, a SimpleInteraction), touched by nothing of the SDK: is it picked and outlined,
/// does its prompt come and go with the player's look? It loads the save: back it up.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> StructuresResearch()
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
        if (agent == null || db == null)
        {
            Log($"Structures: missing: agent {agent != null}, database {db != null}");
            yield break;
        }

        // (A) The templates.
        var rows = new List<(float Area, string Line)>();
        var list = db.CompleteItemInfoList?.itemInfoList;
        for (int i = 0; list != null && i < list.Count; i++)
        {
            var node = list[i]?.TryCast<NodeTemplate>();
            var prefab = node?.nodeStructure;
            if (node == null || prefab == null) continue;
            var go = prefab.gameObject;
            var footprint = go.GetComponentsInChildren<BoxCollider>(true).FirstOrDefault(b => b.name == "Footprint");
            var size = footprint != null ? footprint.size : Vector3.zero;
            var area = go.GetComponentsInChildren<StructureInteractionArea>(true).FirstOrDefault();
            int parts = go.GetComponentsInChildren<BuildPart>(true).Length;
            var interactions = area?.interactions?.ToArray().Select(x => x == null ? "null" : x.GetIl2CppType().Name) ?? [];
            var root = go.GetComponents<Component>().Select(c => c.GetIl2CppType().Name).Where(n => n is not ("Transform" or "NetworkObject" or "NetworkPositionYRotation" or "Structure" or "AttributeManager" or "StructureSurvival" or "StructureNavigation" or "NavMeshInterestArea" or "NetworkLink" or "StructureObjectiveMarker"));
            rows.Add((size.x * size.z, $"{node.name} ({node.GetIl2CppType().Name}) cat '{node.category?.name}' prefab '{go.name}' footprint {size.x:0.0}x{size.y:0.0}x{size.z:0.0} parts {parts} area {(area != null ? "yes" : "no")} [{string.Join(", ", interactions)}] root [{string.Join(", ", root)}] renderers {go.GetComponentsInChildren<MeshRenderer>(true).Length}"));
        }
        File.WriteAllText(Path.Combine(ShotsDir, "structures-small.txt"), string.Join("\n", rows.OrderBy(r => r.Area).Select(r => r.Line)));
        Log($"Structures: {rows.Count} templates written");

        // (B) The well's prefab.
        var wellTemplate = db.GetItemInfo("Item_Structures_WaterWellL1")?.TryCast<NodeTemplate>();
        var wellPrefab = wellTemplate?.nodeStructure?.gameObject;
        if (wellPrefab != null)
        {
            foreach (var part in wellPrefab.GetComponentsInChildren<BuildPart>(true))
                Log($"Structures: well part '{part.name}': geometry [{string.Join(", ", part.geometryObjects?.ToArray().Select(o => o?.name) ?? [])}], completed [{string.Join(", ", part.completedObjects?.ToArray().Select(o => o?.name) ?? [])}], pickable {(part.pickable != null ? part.pickable.name : "-")}");
            foreach (var pick in wellPrefab.GetComponentsInChildren<Pickable>(true))
                Log($"Structures: well pickable '{pick.name}': objects [{string.Join(", ", pick.pickableObjects?.ToArray().Select(o => $"{o?.name}/{(o != null ? LayerMask.LayerToName(o.layer) : "-")}") ?? [])}], volumes {pick.pickableVolumes?.Count}");
            var desc = wellPrefab.GetComponentInChildren<StructureInteractionArea>(true)?.areaDescription?.TryCast<StandaloneAreaDescription>();
            Log($"Structures: well description: name '{desc?.interactionName}' description '{desc?.interactionDescription}' icon {(desc?.icon != null ? desc.icon.name : "-")} format {desc?.descriptionIsFormat}/{desc?.genericDescriptionFormat}");
            Log($"Structures: well preview prefab '{wellTemplate!.nodePreview?.name}': {string.Join(", ", wellTemplate.nodePreview?.GetComponentsInChildren<Component>(true).Select(c => c.GetIl2CppType().Name).Distinct() ?? [])}");
        }

        // (C) An object of our own with an area made as the well's.
        var cam = Camera.main!.transform;
        var forward = cam.forward;
        forward.y = 0;
        forward.Normalize();
        var place = cam.position + forward * 3f;
        place.y = LocalPlayer.Position.y;
        var root2 = new GameObject("UiLab_Stone");
        root2.SetActive(false);
        root2.transform.position = place;
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Model";
        Object.Destroy(cube.GetComponent<Collider>());
        cube.transform.SetParent(root2.transform, false);
        cube.transform.localPosition = new Vector3(0, 1.2f, 0);
        cube.transform.localScale = new Vector3(0.8f, 2.4f, 0.8f);
        cube.layer = LayerMask.NameToLayer("Structure");
        var look = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).FirstOrDefault(r => r.sharedMaterial != null && r.sharedMaterial.shader != null && r.sharedMaterial.shader.name.Contains("Lit"));
        if (look != null) cube.GetComponent<MeshRenderer>().sharedMaterial = look.sharedMaterial;
        var areaObject = new GameObject("InteractionArea");
        areaObject.layer = LayerMask.NameToLayer("Picking");
        areaObject.transform.SetParent(root2.transform, false);
        var area2 = areaObject.AddComponent<StandaloneInteractionArea>();
        var pickable = areaObject.AddComponent<Pickable>();
        var description = ScriptableObject.CreateInstance(Il2CppType.Of<StandaloneAreaDescription>()).Cast<StandaloneAreaDescription>();
        description.interactionName = "Stone";
        description.interactionDescription = "An old standing stone";
        area2.areaDescription = description;
        var linkObject = new GameObject("pickingAndCollision");
        linkObject.layer = areaObject.layer;
        linkObject.transform.SetParent(areaObject.transform, false);
        var trigger = linkObject.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.center = new Vector3(0, 1.2f, 0);
        trigger.size = new Vector3(1.2f, 2.6f, 1.2f);
        var link = linkObject.AddComponent<InteractionAreaLink>();
        link.referenceInteractionArea = areaObject;
        link.SetDefaultTag();
        var useObject = new GameObject("prayInteraction");
        useObject.layer = areaObject.layer;
        useObject.transform.SetParent(areaObject.transform, false);
        var simple = useObject.AddComponent<SimpleInteraction>();
        var template = Resources.FindObjectsOfTypeAll<SimpleInteraction>().FirstOrDefault(s => s.interactSlot != null);
        simple.host = areaObject;
        simple.order = InteractionOrder.PRIMARY;
        simple.mode = template?.mode ?? InteractionMode.CONTINUOUS;
        simple.accessDistance = 2f;
        simple.holdTargetTime = 1.5f;
        simple.interactSlot = template?.interactSlot;
        simple.widgetIcon = template?.widgetIcon;
        simple.widgetMessage = "UiLab pray";
        simple.events ??= new Interaction.InteractionEvents();
        simple.events.OnBegin ??= new UnityEvent();
        simple.events.OnEnd ??= new UnityEvent();
        simple.events.OnFinish ??= new UnityEvent();
        simple.events.OnFinish.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(new Action(() => Log("Structures: ours OnFinish"))));
        area2.interactions ??= new Il2CppSystem.Collections.Generic.List<Interaction>();
        area2.interactions.Add(simple);
        area2.pickables ??= new Il2CppSystem.Collections.Generic.List<Pickable>();
        area2.pickables.Add(pickable);
        pickable.pickableObjects ??= new Il2CppSystem.Collections.Generic.List<GameObject>();
        pickable.pickableObjects.Add(cube);
        pickable.pickableVolumes ??= new Il2CppSystem.Collections.Generic.List<PickableVolume>();
        if (wellPrefab?.GetComponentInChildren<Pickable>(true) is { } wellPick)
        {
            pickable.config = wellPick.config;
            pickable.parameters = wellPick.parameters;
        }
        root2.SetActive(true);
        var widget = Object.FindObjectsByType<InteractWidget>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).FirstOrDefault();
        var pa = agent.Cast<IInteractionAgent>();
        for (int n = 0; n < 6; n++)
        {
            yield return 0.5f;
            Report($"ours {n * 0.5f + 0.5f:0.0} s", agent, pa, area2, widget);
        }
        Log($"Structures: ours pickable registered {pickable.IsRegistered}, selected by picking {GameManagers.Get<PickingManager>()?.SelectedPickable == pickable}");
        Shot("stone-looked-at");
        Log($"Structures: ours Use {simple.Use(pa)}");
        yield return 1f;
        var start = LocalPlayer.Position;
        var side = Vector3.Cross(Vector3.up, forward);
        LocalPlayer.Teleport(start + side * 8f);
        yield return 2.5f;
        Report("ours, the player aside", agent, pa, area2, widget);
        Shot("stone-aside");
        LocalPlayer.Teleport(start);
        yield return 2.5f;
        Report("ours, back", agent, pa, area2, widget);
        Object.Destroy(root2);
        yield return 0.5f;
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    private static void Report(string when, PlayerInteractionAgent agent, IInteractionAgent pa, StandaloneInteractionArea area, InteractWidget? widget)
    {
        var active = agent.GetActiveInteractionArea();
        var users = new List<string>();
        var list = widget?._activeUsersList;
        for (int i = 0; list != null && i < list.Count; i++)
        {
            var user = list[i];
            var session = user?.TryCast<InteractionSession>();
            users.Add(session != null ? $"{session.GetIl2CppType().Name}@{session.Interaction?.name}" : user?.TryCast<Il2CppSystem.Object>()?.GetIl2CppType().Name ?? "?");
        }
        Log($"Structures: {when}: the ray on {(active != null ? $"'{active.GetGameObject()?.name}'" : "nothing")}, area pickers {area.PickersCount} requests {area.RequestsCount} touched {area.IsTouchedByAgent(pa)}, the HUD's users [{string.Join(", ", users)}]");
    }
}
