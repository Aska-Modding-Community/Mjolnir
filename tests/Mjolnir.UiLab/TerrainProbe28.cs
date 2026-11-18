using System;
using System.Collections.Generic;
using Mjolnir.SDK.Terrain;
using SSSGame;
using SandSailorStudio;
using SandSailorStudio.UI;
using UnityEngine;

namespace Mjolnir.UiLab;

/// <summary>
/// The "terrain probe28" scenario: how a placed bush gathered to the end is given its berries back. It places a berry bush,
/// gathers it to the end and tries the gathering's own full replenish, its charges' replenish and its modifier's quantity in
/// turn, logging the available and the maximum count, the modifier's quantity and the stack's count after each. It changes
/// the save's world (not saved by it).
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> TerrainProbe28()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        if (character == null || !Landscape.IsAvailable)
        {
            Log("Probe28: no world");
            yield break;
        }
        var spot = PlaceSpot(character.transform.position);
        foreach (float wait in RegrowthWalk(spot)) yield return wait;
        var bush = NaturalObjects.Place(BushKind, spot);
        yield return 2f;
        var item = PlacedItem(spot, "Mjolnir_World_" + BushKind);
        var gather = item?.gameObject != null ? item.gameObject.GetComponentInChildren<GatherInteraction>(true) : null;
        string State() => Try(() => $"available {gather?.CheckAvailableItemCount()}, maximum {gather?.CheckMaximumItemCount()}, modifier {gather?.GetGatherModifier()?.GetQuantity()}, stack {item?.GetItem()?.count}, counter {(gather?._networkedCount != null)}, info {gather?._gatherInfo?.itemInfo?.name} ×{gather?._gatherInfo?.quantity}, world instance {(gather?._worldInstance != null)}");
        Log($"Probe28: placed: {State()}");
        for (int charge = 0; charge < 8 && bush != null && bush.GatherCount > 0; charge++)
        {
            bush.Gather(1);
            yield return 1f;
        }
        Log($"Probe28: gathered: {State()}");
        Try(() => { gather?.ReplenishFullQuantity(); return "ok"; });
        yield return 1.5f;
        Log($"Probe28: after ReplenishFullQuantity: {State()}");
        Try(() => { gather?.ReplenishCharges(1); return "ok"; });
        yield return 1.5f;
        Log($"Probe28: after ReplenishCharges(1): {State()}");
        Try(() => { gather?.GetGatherModifier()?.SetQuantity(5); gather?.RefreshInteractionData(); return "ok"; });
        yield return 1.5f;
        Log($"Probe28: after the modifier's SetQuantity(5): {State()}");
        int got = bush?.Gather(1) ?? -1;
        yield return 1.5f;
        Log($"Probe28: a charge gathered then gave {got}: {State()}");
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }
}
