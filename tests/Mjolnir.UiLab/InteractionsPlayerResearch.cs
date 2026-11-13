using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SSSGame;
using SSSGame.Render;
using SandSailorStudio;
using SandSailorStudio.UI;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "interactions player" scenario: an interaction of our own that the player can use as the game's markers are used. A
/// game marker's SimpleInteraction is logged as the model (its area, pickable, layers, the picking manager's layer); then an
/// altar is made in code in front of the player (a cube, a StandaloneInteractionArea, a Pickable, a SimpleInteraction with the
/// marker's slot and icon), the player's picked area is followed, the player's session is started and its hold filled, and a
/// villager uses it too, every interaction event logged. It loads the save: back it up.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> InteractionsPlayerResearch()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 8f;
        KeepDaytime();
        yield return 3f;
        var player = GameManagers.Get<PlayerManager>()?.LocalPlayer;
        var character = player?.playerCharacter;
        var agent = player?.playerInteractionAgent?.TryCast<PlayerInteractionAgent>();
        if (character == null || agent == null)
        {
            Log("Interactions: no player");
            yield break;
        }
        var pm = GameManagers.Get<PickingManager>();
        Log($"Interactions: picking layer {LayerNames(pm?.pickLayer.value ?? 0)}, occlusion {LayerNames(pm?.occlusionLayer.value ?? 0)}, draw distance {pm?.pickableDrawDistance}");

        // The model: a marker's SimpleInteraction.
        var model = Object.FindObjectsByType<SimpleInteraction>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .OrderBy(s => s.name == "StructureInteraction" ? 0 : 1).FirstOrDefault();
        if (model != null)
        {
            var area = model.GetComponentInParent<StandaloneInteractionArea>();
            var pick = model.GetComponentInParent<Pickable>();
            Log($"Interactions: model '{PathOf(model.transform)}' layer {LayerMask.LayerToName(model.gameObject.layer)}, events {model.events != null}, " +
                $"area {(area != null ? $"{area.GetIl2CppType().Name} on '{area.name}' layer {LayerMask.LayerToName(area.gameObject.layer)}, interactions {area.interactions?.Count}, pickables {area.pickables?.Count}, description {(area.areaDescription != null ? area.areaDescription.GetIl2CppType().Name : "-")}, name '{Safe(area.GetName)}' desc '{Safe(area.GetDescription)}'" : "none")}, " +
                $"pickable {(pick != null ? $"on '{pick.name}' objects [{string.Join(", ", pick.pickableObjects?.ToArray().Select(o => $"{o.name}/{LayerMask.LayerToName(o.layer)}") ?? [])}] volumes {pick.pickableVolumes?.Count} config {(pick.config != null ? pick.config.name : "-")} area {pick.InteractionArea != null}" : "none")}");
        }

        Log($"Interactions: the player's ray mask {LayerNames(agent.raycastLayerMask.value)}, radius {agent.rayRadius}, length {agent.rayLength}");
        if (model != null)
        {
            var area = model.GetComponentInParent<StandaloneInteractionArea>();
            if (area != null) foreach (var c in area.GetComponentsInChildren<Collider>(true))
                Log($"Interactions:   model collider '{PathOf(c.transform)}' {c.GetIl2CppType().Name} layer {LayerMask.LayerToName(c.gameObject.layer)} trigger {c.isTrigger} tag {c.tag} rigidbody {c.attachedRigidbody != null} | {Components(c.gameObject)}");
            foreach (var v in (IEnumerable<PickableVolume>?)model.GetComponentInParent<Pickable>()?.pickableVolumes?.ToArray() ?? [])
                Log($"Interactions:   model volume '{PathOf(v.transform)}' layer {LayerMask.LayerToName(v.gameObject.layer)} tag {v.tag} pickable {v.pickable != null} | {Components(v.gameObject)}");
        }

        // Ours, in front of the camera within the player's ray (from the camera): one object with the model, the collider, the
        // area, the pickable and its volume.
        var cam = Camera.main!.transform;
        var forward = cam.forward;
        forward.y = 0;
        forward.Normalize();
        var place = cam.position + cam.forward * 3f;

        var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
        root.name = "UiLab_Altar";
        root.SetActive(false);
        root.transform.position = place;
        root.transform.localScale = new Vector3(0.6f, 1.2f, 0.6f);
        int layer = model != null ? model.gameObject.layer : LayerMask.NameToLayer("Structure");
        root.layer = layer;
        var look = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .FirstOrDefault(r => r.sharedMaterial != null && r.sharedMaterial.shader != null && r.sharedMaterial.shader.name.Contains("Lit"));
        if (look != null) root.GetComponent<MeshRenderer>().sharedMaterial = look.sharedMaterial;
        Log($"Interactions: ours at {Vector3.Distance(place, cam.position):0.0} m from the camera, material {(look != null ? look.sharedMaterial.name : "-")}");
        var areaOurs = root.AddComponent<StandaloneInteractionArea>();
        var pickOurs = root.AddComponent<Pickable>();
        var modelVolume = model?.GetComponentInParent<Pickable>()?.pickableVolumes?.ToArray().Select(v => v.TryCast<PickableRendererVolume>()).FirstOrDefault(v => v != null);
        if (modelVolume != null)
            Log($"Interactions: the model's renderer volume: selection {modelVolume.drawSelection} outline {modelVolume.drawOutline} scan {modelVolume.scanMaterials} submeshes {modelVolume.subMeshMask:X}");
        var volume = root.AddComponent<PickableRendererVolume>();
        volume.pickable = pickOurs;
        volume.drawSelection = modelVolume?.drawSelection ?? true;
        volume.drawOutline = modelVolume?.drawOutline ?? true;
        volume.scanMaterials = modelVolume?.scanMaterials ?? false;
        volume.subMeshMask = modelVolume?.subMeshMask ?? uint.MaxValue;
        var interact = new GameObject("UiLab_Interact");
        interact.transform.SetParent(root.transform, false);
        var ours = interact.AddComponent<SimpleInteraction>();
        ours.host = root;
        ours.holdTargetTime = 1.5f;
        ours.accessDistance = 3f;
        ours.order = InteractionOrder.PRIMARY;
        ours.mode = model?.mode ?? InteractionMode.CONTINUOUS;
        ours.widgetMessage = "UiLab altar";
        ours.widgetIcon = model?.widgetIcon;
        ours.interactSlot = model?.interactSlot;
        ours.events ??= new Interaction.InteractionEvents();
        ours.events.OnBegin ??= new UnityEvent();
        ours.events.OnEnd ??= new UnityEvent();
        ours.events.OnFinish ??= new UnityEvent();
        ours.events.OnBegin.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(new Action(() => Log("Interactions: event OnBegin"))));
        ours.events.OnEnd.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(new Action(() => Log("Interactions: event OnEnd"))));
        ours.events.OnFinish.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(new Action(() => Log("Interactions: event OnFinish"))));
        areaOurs.interactions ??= new Il2CppSystem.Collections.Generic.List<Interaction>();
        areaOurs.interactions.Add(ours);
        areaOurs.pickables ??= new Il2CppSystem.Collections.Generic.List<Pickable>();
        areaOurs.pickables.Add(pickOurs);
        pickOurs.pickableObjects ??= new Il2CppSystem.Collections.Generic.List<GameObject>();
        pickOurs.pickableVolumes ??= new Il2CppSystem.Collections.Generic.List<PickableVolume>();
        pickOurs.pickableVolumes.Add(volume);
        if (model?.GetComponentInParent<Pickable>() is { } modelPick)
        {
            pickOurs.config = modelPick.config;
            pickOurs.parameters = modelPick.parameters;
        }
        root.SetActive(true);
        yield return 0.5f;
        Log($"Interactions: ours active, pickable registered {pickOurs.IsRegistered}, area interactable {Safe(() => areaOurs.IsInteractable.ToString())}, pickable area {pickOurs.InteractionArea != null}, interaction's area {Safe(() => (ours.GetInteractionArea() != null).ToString())}, pickable {Safe(() => (ours.GetPickable() != null).ToString())}");

        for (int n = 0; n < 8; n++)
        {
            yield return 0.5f;
            var active = agent.GetActiveInteractionArea();
            Log($"Interactions:   {n * 0.5f + 0.5f:0.0} s: the player's area {(active != null ? $"'{active.GetGameObject()?.name}' ours {active.GetGameObject() == root}" : "none")}, touched {Safe(() => ours.IsTouchedByAgent(agent.Cast<IInteractionAgent>()).ToString())}");
        }
        Shot("interaction-aimed");
        Log($"Interactions: the input action for ours {Safe(() => agent._GetInputAction(ours)?.name ?? "none")}");

        Log($"Interactions: probes {UiLab.Instance.Harmony.CreateClassProcessor(typeof(InteractionProbes)).Patch()?.Count ?? 0} method(s) patched");
        InteractionProbes.Only = "UiLab";
        InteractionProbes.Watching = true;

        // The player's session, its hold filled by hand.
        var pa = agent.Cast<IInteractionAgent>();
        Log($"Interactions: before Use: area touched {Safe(() => areaOurs.IsTouchedByAgent(pa).ToString())}, requested {Safe(() => areaOurs.IsRequestedByAgent(pa).ToString())}, pickers {areaOurs.PickersCount}, requests {areaOurs.RequestsCount}, interaction touched {Safe(() => ours.IsTouchedByAgent(pa).ToString())}, touchy sessions {ours._touchyAgentsTable?.Count}");
        try
        {
            areaOurs.Touch(pa, true);
        }
        catch (Exception e)
        {
            Log($"Interactions: Touch threw {e.GetType().Name} {e.Message}");
        }
        yield return 0.5f;
        Log($"Interactions: after Touch: area touched {Safe(() => areaOurs.IsTouchedByAgent(pa).ToString())}, pickers {areaOurs.PickersCount}");
        Shot("interaction-touched");
        bool used = false;
        try
        {
            used = ours.Use(agent.Cast<IInteractionAgent>());
        }
        catch (Exception e)
        {
            Log($"Interactions: player Use threw {e.GetType().Name} {e.Message}");
        }
        Log($"Interactions: player Use {used}, sessions {ours.ActiveSessionsCount}");
        for (int n = 0; n < 8; n++)
        {
            yield return 0.5f;
            var session = ours.FindActiveSession(agent.Cast<IInteractionAgent>())?.TryCast<PlayerSimpleInteractionConfig.PlayerSimpleInteractionSession>();
            Log($"Interactions:   {n * 0.5f + 0.5f:0.0} s: player's session {(session != null ? $"{session.SessionState} hold {session._holdTimer:0.00}" : "none")}");
            if (n == 1 && session != null) Shot("interaction-holding");
        }

        // A villager.
        var villager = Object.FindObjectsOfType<Villager>().Where(v => !v.IsDead).OrderBy(v => Vector3.Distance(v.transform.position, place)).FirstOrDefault();
        if (villager != null)
        {
            bool vused = false;
            try
            {
                vused = ours.Use(villager.Cast<IInteractionAgent>());
            }
            catch (Exception e)
            {
                Log($"Interactions: villager Use threw {e.GetType().Name} {e.Message}");
            }
            Log($"Interactions: villager {villager.name} at {Vector3.Distance(villager.transform.position, place):0.#} m, Use {vused}");
            yield return 2f;
        }
        Object.Destroy(root);
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    private static string Safe(Func<string> read)
    {
        try
        {
            return read();
        }
        catch (Exception e)
        {
            return $"threw {e.GetType().Name}";
        }
    }

    private static string LayerNames(int mask) => string.Join("|", Enumerable.Range(0, 32).Where(i => (mask & (1 << i)) != 0).Select(LayerMask.LayerToName));
}
