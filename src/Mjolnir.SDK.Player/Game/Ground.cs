using SSSGame;
using UnityEngine;

namespace Mjolnir.SDK.Player;

/// <summary>
/// The ground at a place in the world: what a ray from high above hits, past the
/// game's trigger volumes (weather, status effects) and characters. The player's own capsules lie on the Character layer
/// but outside the character's hierarchy, so that layer is left out of the ray, and other characters by their parent.
/// The terrain wins over what stands on it, so a teleport lands beside a building rather than on its roof; without
/// terrain there (a tile still streaming, a cliff of rock) the highest solid thing counts. Nothing while the world there
/// has not streamed in.
/// </summary>
internal static class Ground
{
    private const float Top = 2000f;
    private static int s_terrainLayer = int.MinValue;
    private static int s_mask;

    public static float? HeightAt(float x, float z)
    {
        if (s_terrainLayer == int.MinValue)
        {
            s_terrainLayer = LayerMask.NameToLayer("Terrain");
            int characters = LayerMask.NameToLayer("Character");
            s_mask = characters >= 0 ? Physics.DefaultRaycastLayers & ~(1 << characters) : Physics.DefaultRaycastLayers;
        }
        var hits = Physics.RaycastAll(new Vector3(x, Top, z), Vector3.down, 2 * Top, s_mask);
        float? terrain = null, solid = null;
        float terrainDistance = float.MaxValue, solidDistance = float.MaxValue;
        for (int i = 0; i < hits.Length; i++)
        {
            var hit = hits[i];
            var collider = hit.collider;
            if (collider == null || collider.isTrigger) continue;
            if (collider.gameObject.layer == s_terrainLayer)
            {
                if (hit.distance < terrainDistance)
                {
                    terrainDistance = hit.distance;
                    terrain = hit.point.y;
                }
            }
            else if (hit.distance < solidDistance && collider.GetComponentInParent<Character>() == null)
            {
                solidDistance = hit.distance;
                solid = hit.point.y;
            }
        }
        return terrain ?? solid;
    }
}
