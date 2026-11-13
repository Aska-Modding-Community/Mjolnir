using UnityEngine;

namespace Mjolnir.ModKit
{
    /// <summary>
    /// Draws an imported skeleton of the game in the Scene view: a line from each bone to its parent and a dot on each joint,
    /// the left side blue, the right red. It is only the kit's: the skeleton prefabs never go into a bundle.
    /// </summary>
    [ExecuteAlways]
    public sealed class RigGizmo : MonoBehaviour
    {
        public float JointSize = 0.012f;

        /// <summary>Whether the lines are drawn; off on a skeleton that wears the game's model (the kit's Import Rigs).</summary>
        public bool ShowSkeleton = true;

        private void OnDrawGizmos()
        {
            if (!ShowSkeleton) return;
            foreach (var bone in GetComponentsInChildren<Transform>())
            {
                if (bone == transform || bone.parent == null || bone.parent == transform) continue;
                Gizmos.color = bone.name.Contains(" L ") ? new Color(0.3f, 0.6f, 1f) : bone.name.Contains(" R ") ? new Color(1f, 0.4f, 0.3f) : new Color(1f, 0.85f, 0.3f);
                Gizmos.DrawLine(bone.parent.position, bone.position);
                Gizmos.DrawSphere(bone.position, JointSize);
            }
        }
    }
}
