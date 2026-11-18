using System;

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// What <see cref="LandscapeObjects.Harvest"/> does to each natural object a change of the ground reaches: what the game
/// harvests (a tree, a stump, a stone clump) is harvested with its loot; what is picked by hand (a bush, a crop, a nest)
/// gives all it has and is then cleared; the rest (a boulder of the scenery, grass, anything not brought up round the
/// player) is cleared as <see cref="NaturalObject.Clear"/> does. The items lying there are left.
/// </summary>
internal static class ObjectSweep
{
    /// <summary>The most charges taken out of a thing picked by hand: more than any of the game's has.</summary>
    private const int MostCharges = 32;

    /// <summary>Takes one object away with its loot; false when the game keeps it.</summary>
    public static bool Take(IWorldObject found, HarvestOptions options)
    {
        if (found.IsGone) return true;
        if (found.Kind == NaturalObjectKind.Item) return false;
        if (found.Kind != NaturalObjectKind.Grass && found.Harvest(options)) return true;
        for (int charge = 0; charge < MostCharges && found.GatherCount > 0; charge++)
            if (found.Gather(1) <= 0) break;
        return found.Clear();
    }

    /// <summary>Takes each object, a failure logged and passed by.</summary>
    public static void TakeAll(System.Collections.Generic.IEnumerable<IWorldObject> objects, HarvestOptions options)
    {
        foreach (var found in objects)
        {
            try
            {
                Take(found, options);
            }
            catch (Exception e)
            {
                SdkLog.Warn(Landscape.Platform, $"An object could not be harvested: {e.Message}");
            }
        }
    }
}
