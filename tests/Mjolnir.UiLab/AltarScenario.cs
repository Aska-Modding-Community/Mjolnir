using System.Collections.Generic;
using System.Linq;
using Mjolnir.SDK;
using Mjolnir.SDK.AI;
using Mjolnir.SDK.Player;
using SSSGame;
using SSSGame.Render;
using SandSailorStudio;
using SandSailorStudio.Inventory;
using SandSailorStudio.UI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "altar" scenario (with -Examples, AltarExample turned on): AltarExample's altar, a building of the mod's own with
/// its model, placed in front of the camera as the player's builder places it once the ground is level
/// (Settlement.CreateStructure, SpawnedByTool), and built as the player builds: its template's settings (placement tool,
/// category, what dismantling gives back), its stages (the construction look in the game's construction material while it
/// waits for its supplies and while it is built, the model once it stands: screenshots), its supplies put in and its work
/// added as hits add it. Then its pickables, its prayer in the building's own area, the player's look on it and the game's
/// card (a screenshot), the prayer as the key's press makes it (stamina back), the player stepping aside and back (the
/// prompt must go and come back with the look: screenshots). The altar is dismantled at the end. It loads the save: back
/// it up.
/// </summary>
internal static partial class Autopilot
{
    private const string AltarAsset = "Item_mjolnir.altarexample_altar";

    private static IEnumerator<float> AltarScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 8f;
        KeepDaytime();
        yield return 3f;
        var agent = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerInteractionAgent?.TryCast<PlayerInteractionAgent>();
        var db = GameManagers.Get<ItemDatabaseManager>()?.Database;
        var settlement = GameManagers.Get<SettlementManager>()?.worldSettlement;
        Log($"Altar: stones in the game: {string.Join(", ", Mjolnir.SDK.Content.Items.All.Where(i => i.AssetName.Contains("Stone") || i.AssetName.Contains("Rock")).Select(i => $"{i.AssetName} '{i.DisplayName}'").Take(20))}");
        var template = db?.GetItemInfo(AltarAsset)?.TryCast<StructureTemplate>();
        if (agent == null || settlement == null || template == null)
        {
            Log($"Altar: missing: agent {agent != null}, settlement {settlement != null}, the altar's template {template != null}");
            yield break;
        }
        var pa = agent.Cast<IInteractionAgent>();

        // The altar in front of the camera, its front to the player.
        var cam = Camera.main!.transform;
        var forward = cam.forward;
        forward.y = 0;
        forward.Normalize();
        var at = cam.position + forward * 3.2f;
        at = GroundPoint(at) ?? at;
        // The player's look picks what the screen's middle shows: with the camera level its ray passes over the low altar, so
        // the altar stands where the ray is 0.7 m above its foot (lifted off the ground when need be; the place on the
        // ground is checked by the footprint below).
        var middle = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        var flat = new Vector3(middle.direction.x, 0, middle.direction.z).magnitude;
        if (flat > 0.01f)
        {
            float onRay = (middle.origin + middle.direction * (3.2f / flat)).y - 0.7f;
            if (onRay > at.y) at.y = onRay;
        }
        Log($"Altar: placed {at.y - (GroundPoint(at) ?? at).y:0.00} m above the ground, on the screen's middle");
        var placement = new PointPlacement();
        placement.position = at;
        placement.rotation = Quaternion.LookRotation(-forward, Vector3.up);
        Log($"Altar: the template: placement {template.placementTool?.GetIl2CppType().Name}, category {template.category?.name}, " +
            $"dismantle parts {template.dismantlePartsConfig?.name} [{string.Join("; ", Enumerable.Range(0, template.dismantlePartsConfig?.subparts?.Count ?? 0).Select(i => Cost(template.dismantlePartsConfig!.subparts[i])))}]");
        // The place checked as the player's placement checks it: a preview made there and its footprint validated.
        foreach (string asset in new[] { "Item_Decoration_DecorativeBirch", "Item_StoneLantern", AltarAsset })
        {
            var t = db?.GetItemInfo(asset)?.TryCast<StructureTemplate>();
            if (t == null) continue;
            StructurePreviewData? preview = null;
            try
            {
                preview = t.CreatePreview(placement);
            }
            catch (System.Exception e)
            {
                Log($"Altar: {asset}'s preview failed: {e.Message}");
            }
            if (preview == null) continue;
            yield return 0.5f;
            try
            {
                var node = preview.TryCast<NodeStructurePreviewData>();
                var logic = node?.structureLogic;
                string tool = "";
                if (logic != null && t.placementTool != null)
                {
                    var mask = t.placementTool.footprintCheckMask;
                    bool free = logic.ValidateFootprint(mask, (Collider)null!);
                    var box = logic.FootprintCollider;
                    var hits = box == null ? [] : Physics.OverlapBox(box.transform.TransformPoint(box.center), Vector3.Scale(box.size, box.transform.lossyScale) / 2, box.transform.rotation, mask.value)
                        .Where(c => c != null && c.Pointer != box.Pointer).Select(c => $"{c.name} [{LayerMask.LayerToName(c.gameObject.layer)}] of {c.transform.root.name}").ToArray();
                    tool = $", the tool's footprint check {(free ? "free" : "BLOCKED")} by [{string.Join(", ", hits)}]";
                }
                Log($"Altar: {asset} placed here would be {t.ValidateFootprint(preview, placement)}{tool}");
                t.DestroyPreview(preview, false);
            }
            catch (System.Exception e)
            {
                Log($"Altar: {asset}'s check failed: {e.Message}");
            }
            yield return 0.5f;
        }
        var lanternTemplate = db?.GetItemInfo("Item_StoneLantern")?.TryCast<StructureTemplate>();
        if (lanternTemplate != null)
        {
            Structure? lantern = null;
            try
            {
                lantern = settlement.CreateStructure(lanternTemplate, placement, null, StructureTemplate.Options.SpawnedByTool, 0);
            }
            catch (System.Exception e)
            {
                Log($"Altar: the lantern failed: {e.Message}");
            }
            if (lantern != null)
            {
                yield return 2f;
                Log($"Altar: the lantern under way: {LookFinds(agent, lantern)}");
                try
                {
                    lantern.Dismantle(true);
                }
                catch (System.Exception e)
                {
                    Log($"Altar: dismantling the lantern failed: {e.Message}");
                }
                yield return 3f;
            }
        }
        Structure? built = null;
        try
        {
            // As the player's builder places it once the ground is level (TerraformingGrid.TrySpawnResultStructure).
            built = settlement.CreateStructure(template, placement, null, StructureTemplate.Options.SpawnedByTool, 0);
        }
        catch (System.Exception e)
        {
            Log($"Altar: building failed: {e}");
        }
        if (built == null) yield break;
        {
            var network = built.GetComponent<Fusion.NetworkObject>();
            var spawner = GameManagers.Get<SSSGame.Network.NetworkSpawner>();
            Fusion.NetworkObject? prefab = null;
            bool known = network != null && spawner != null && spawner.TryGetPrefabFromSpawnedObject(network, out prefab);
            Log($"Altar: its network prefab: {(known ? prefab?.name : "unknown")} (the template's {template.TryCast<NodeTemplate>()?.nodeStructure?.name}), " +
                $"guid {network?.NetworkGuid}, flags {network?.Flags}");
        }
        yield return 2f;

        // Its stages: the construction look while it waits for supplies and while it is built, the model once it stands.
        GameObject? Look(string name) => built.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name)?.gameObject;
        string Looks()
        {
            var construction = Look("Mjolnir Model (construction)");
            var finished = Look("Mjolnir Model");
            var renderer = construction?.GetComponentInChildren<Renderer>(true);
            return $"construction {(construction == null ? "MISSING" : construction.activeInHierarchy ? "shown" : "hidden")} ({renderer?.sharedMaterial?.name}), " +
                   $"model {(finished == null ? "MISSING" : finished.activeInHierarchy ? "shown" : "hidden")}; {StructureState(built)}";
        }
        Log($"Altar: placed: {Looks()}");
        Log($"Altar: placed, the look finds: {LookFinds(agent, built)}");
        Shot("altar-placed");
        var part = built.buildSite?.GetCurrentLayer()?.parts?[0];
        if (part != null)
        {
            Log($"Altar: its first stage asks [{Cost(part.container?.itemConfig)}], work {part.buildInteraction?.buildVolume}");
            var cost = part.container?.itemConfig?.TryCast<ItemManifestConfig>()?.items;
            if (cost != null) built.buildSite!._FillPartWithItems(part, cost);
            yield return 1.5f;
            Log($"Altar: supplied: {Looks()}");
            Log($"Altar: supplied, the look finds: {LookFinds(agent, built)}");
            Shot("altar-supplied");
            float half = part.buildInteraction.TargetBuildVolume / 2;
            part.buildInteraction.RequestAddBuildVolume(ref half);
            yield return 1.5f;
            Log($"Altar: half built: {Looks()}");
            Shot("altar-half-built");
            float rest = part.buildInteraction.TargetBuildVolume - part.buildInteraction.CurrentBuildVolume;
            part.buildInteraction.RequestAddBuildVolume(ref rest);
        }
        yield return 3f;
        Log($"Altar: built: {Looks()}");

        var model = built.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Mjolnir Model");
        var area = built.GetComponentInChildren<StructureInteractionArea>(true);
        var ours = area?.GetComponentsInChildren<SimpleInteraction>(true).FirstOrDefault(s => s.name.StartsWith("Mjolnir "));
        var shown = built.GetComponentsInChildren<Renderer>(true).Where(r => r.isVisible).Select(r => r.name).Distinct().ToList();
        Log($"Altar: '{built.name}' template {built.Template?.name} name '{built.StructureName}', the model {(model != null ? "there" : "MISSING")}, renderers seen [{string.Join(", ", shown)}], " +
            $"pickables [{string.Join("; ", built.GetComponentsInChildren<Pickable>(true).Select(p => $"{p.name}: {string.Join(", ", p.pickableObjects?.ToArray().Select(o => o?.name) ?? [])}"))}], " +
            $"the area's interactions [{string.Join(", ", area?.interactions?.ToArray().Select(i => i == null ? "null" : $"{i.GetIl2CppType().Name} '{i.name}'") ?? [])}], ours '{ours?.widgetMessage}' hold {ours?.holdTargetTime}");
        var altar = Interactions.All.FirstOrDefault(i => i.ModId == "mjolnir.altarexample" && i.Target != null && i.Target.Pointer == built.gameObject.Pointer);
        altar?.WithMaxUsers(3);
        var loc = SandSailorStudio.Localization.LocalizationManager.Instance;
        foreach (string token in new[] { "name", "desc" })
        {
            string key = template.GetKey(token);
            string? main = null, fallback = null;
            if (loc?._localizedStrings != null && loc._localizedStrings.ContainsKey(key)) main = loc._localizedStrings[key];
            if (loc?._localizedFallbackStrings != null && loc._localizedFallbackStrings.ContainsKey(key)) fallback = loc._localizedFallbackStrings[key];
            string cached = "";
            bool inCache = loc?._cache != null && loc._cache.TryGetValue(key, out cached);
            Log($"Altar: text '{token}': key '{key}', main '{main}', fallback '{fallback}', cached {inCache} '{cached}', Loc '{template.Loc(token, new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray(0L))}', language {Localization.Language}");
        }
        Log($"Altar: the interaction {(altar != null ? $"{altar} '{altar.Text}' for the player {altar.PlayerCanUse}" : "MISSING")}; all {Interactions.All.Count}");
        if (ours != null)
        {
            var ia = ours.interactable;
            Log($"Altar: ours: pickable '{ours.GetPickable()?.name}', area '{(ours.GetInteractionArea()?.GetGameObject()?.name)}', interactable {(ia != null ? ia.TryCast<Il2CppSystem.Object>()?.GetIl2CppType().Name : "none")}, " +
                $"session id {(ia != null ? ia.SessionID(ours).ToString() : "-")}, can interact {(ia != null ? ia.CanInteract(ours).ToString() : "-")}, enabled {ours.enabled}, active {ours.gameObject.activeInHierarchy}, order {ours.order}, mode {ours.mode}");
            var menu = area!.interactions.ToArray().FirstOrDefault(i => i != null && i.GetIl2CppType().Name == "StructureInteraction");
            if (menu != null && ia != null)
                Log($"Altar: the building's menu: pickable '{menu.GetPickable()?.name}', session id {ia.SessionID(menu)}, can interact {ia.CanInteract(menu)}, order {menu.order}, mode {menu.mode}");
            Log($"Altar: probes {UiLab.Instance.Harmony.CreateClassProcessor(typeof(AltarProbes)).Patch()?.Count ?? 0} method(s) patched");
            AltarProbes.Watching = true;
        }
        var widget = Object.FindObjectsByType<InteractWidget>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).FirstOrDefault();
        for (int n = 0; n < 4; n++)
        {
            var users = new List<string>();
            var wl = widget?._activeUsersList;
            for (int u = 0; wl != null && u < wl.Count; u++)
            {
                var session = wl[u]?.TryCast<InteractionSession>();
                users.Add(session != null ? $"{session.GetIl2CppType().Name}@{(session.Interaction == null ? "gone" : session.Interaction.name)}" : wl[u]?.TryCast<Il2CppSystem.Object>()?.GetIl2CppType().Name ?? "?");
            }
            Log($"Altar:   the HUD's users [{string.Join(", ", users)}]");
            yield return 0.5f;
            var active = agent.GetActiveInteractionArea();
            Log($"Altar:   {n * 0.5f + 0.5f:0.0} s: the ray on {(active != null ? $"'{active.GetGameObject()?.name}' of '{active.GetGameObject()?.GetComponentInParent<Structure>()?.name}'" : "nothing")}, the card '{area?.GetName()}' / '{area?.GetDescription()}', " +
                $"touched {(area != null && area.IsTouchedByAgent(pa))}, picked {GameManagers.Get<PickingManager>()?.SelectedPickable?.name}");
        }
        Shot("altar-looked-at");

        // The prayer, as the key's press makes it.
        Log($"Altar: names: template '{template.Name}' (localized '{template.localizedName}'), structure '{built.StructureName}', default '{built.DefaultName}'");
        if (ours != null && area != null && agent.GetActiveInteractionArea()?.GetGameObject() == area.gameObject)
        {
            LocalPlayer.Stamina.Value = LocalPlayer.Stamina.Maximum * 0.3f;
            yield return 0.5f;
            float before = LocalPlayer.Stamina.Value;
            Log($"Altar: the prayer: Use {ours.Use(pa)}, stamina {before:0}");
            yield return 2.5f;
            Log($"Altar: stamina after it {LocalPlayer.Stamina.Value:0}/{LocalPlayer.Stamina.Maximum:0}, locked {agent.LockActiveInteraction}");
        }
        else Log("Altar: not looked at: no prayer by the player");

        // The player steps aside and back: the prompt goes with the look and comes back with it.
        var start = LocalPlayer.Position;
        var side = Vector3.Cross(Vector3.up, forward);
        LocalPlayer.Teleport(start + side * 9f);
        yield return 2.5f;
        Shot("altar-aside");
        Log($"Altar: aside: the ray on {(agent.GetActiveInteractionArea() is { } a ? $"'{a.GetGameObject()?.name}'" : "nothing")}");
        LocalPlayer.Teleport(start);
        yield return 2.5f;
        Shot("altar-back");
        Log($"Altar: back: the ray on {(agent.GetActiveInteractionArea() is { } b ? $"'{b.GetGameObject()?.name}'" : "nothing")}");

        DevConsole.Execute("interactions");
        yield return 0.5f;
        try
        {
            built.Dismantle(true);
        }
        catch (System.Exception e)
        {
            Log($"Altar: dismantling failed: {e.Message}");
        }
        yield return 1f;
        AltarProbes.Watching = false;
        Log($"Altar: after the dismantling: interactions {Interactions.All.Count}");
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    // What the player's look finds on a structure: the active area, the picked pickable, and the structure's pickables with
    // their objects and volumes; a build part's construction tint as the game sets it on its geometry.
    private static string LookFinds(PlayerInteractionAgent agent, Structure s)
    {
        var active = agent.GetActiveInteractionArea();
        var picked = GameManagers.Get<PickingManager>()?.SelectedPickable;
        var pickables = s.GetComponentsInChildren<Pickable>(true).Select(p =>
        {
            var objects = p.pickableObjects;
            var names = new List<string>();
            for (int i = 0; objects != null && i < objects.Count; i++) names.Add(objects[i] == null ? "null" : $"{objects[i].name}{(objects[i].activeInHierarchy ? "" : " (off)")}");
            var volumes = p.GetComponentsInChildren<PickableVolume>(true).Length;
            return $"{p.name}{(p.isActiveAndEnabled ? "" : " (off)")} [{string.Join(", ", names)}] volumes {volumes} registered {p.IsRegistered} active {p._active} renderers {p._renderers?.Count} list {p._pickableRendererList?.Count} area {p.InteractionArea?.GetGameObject()?.name}";
        });
        string tint = "";
        var part = s.buildSite?.GetCurrentLayer()?.parts?[0];
        var geometry = part?.geometryObjects;
        for (int i = 0; geometry != null && i < geometry.Count; i++)
        {
            var r = geometry[i]?.GetComponent<Renderer>();
            var m = r?.sharedMaterial;
            if (m == null || !m.HasProperty("_ConstructionProgression")) continue;
            tint += $" {geometry[i].name}: progression {m.GetFloat("_ConstructionProgression")} selection {m.GetFloat("_ConstructionSelection")} ({m.name})";
        }
        var ours = s.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Mjolnir Model (construction)")?.GetComponentInChildren<Renderer>(true)?.sharedMaterial;
        if (ours != null && ours.HasProperty("_ConstructionProgression"))
            tint += $" ours: progression {ours.GetFloat("_ConstructionProgression")} selection {ours.GetFloat("_ConstructionSelection")} ({ours.name})";
        return $"area {(active != null ? $"{active.GetGameObject()?.name} ({active.TryCast<Il2CppSystem.Object>()?.GetIl2CppType().Name}) '{active.GetName()}' / '{active.GetDescription()}'" : "none")}, " +
               $"picked {picked?.name ?? "none"}, pickables {string.Join("; ", pickables)}; tint{tint}";
    }
}

/// <summary>The touches of the altar's interactions: ours and the building's own menu.</summary>
[HarmonyLib.HarmonyPatch]
internal static class AltarProbes
{
    public static bool Watching;
    private static int s_count;

    [HarmonyLib.HarmonyPostfix]
    [HarmonyLib.HarmonyPatch(typeof(Interaction), "_OnPickableTouch")]
    private static void Touch(Interaction __instance, IInteractionAgent __0, bool __1)
    {
        if (!Watching || s_count >= 30 || __instance.GetComponentInParent<Structure>()?.Template?.name != "Item_mjolnir.altarexample_altar") return;
        s_count++;
        UiLab.Instance.Log.Info($"[UiLab] Altar probe: _OnPickableTouch {__instance.GetIl2CppType().Name} '{__instance.name}' touch {__1}, touchy {__instance._touchyAgentsTable?.Count}");
    }

    [HarmonyLib.HarmonyPostfix]
    [HarmonyLib.HarmonyPatch(typeof(PlayerSimpleInteractionConfig.PlayerSimpleInteractionSession), nameof(InteractionSession.Touch))]
    private static void SessionTouch(InteractionSession __instance)
    {
        if (!Watching || s_count >= 30 || !(__instance.Interaction?.name?.StartsWith("Mjolnir ") ?? false)) return;
        s_count++;
        UiLab.Instance.Log.Info($"[UiLab] Altar probe: our session touched");
    }
}
