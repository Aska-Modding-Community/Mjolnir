using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Mjolnir.ModKit.Examples
{
    /// <summary>
    /// The model of Mjolnir.AltarExample's altar, made in code so it can be made again anywhere: a low slab of grey stone
    /// with a runestone standing on its back half, its top rounded, runes carved down its face and painted red as old
    /// runestones were, inside a carved border. Two HDRP/Lit materials with textures drawn here (the stone, and the runestone's
    /// face). The origin is on the ground under the slab's middle, the runes face +Z, the building's front. It goes
    /// into Assets/Mods/Mjolnir.AltarExample/models, the example's "models" bundle (BundleBuilder), with a box collider
    /// round it.
    /// </summary>
    public static class AltarModel
    {
        private const string Folder = "Assets/Mods/Mjolnir.AltarExample/models";

        // Metres. The slab, and the runestone on its back half: its box up to StoneHeight, a half round top above.
        private const float SlabWidth = 1.1f, SlabHeight = 0.2f, SlabDepth = 0.75f;
        private const float StoneWidth = 0.5f, StoneHeight = 1.05f, StoneDepth = 0.22f, StoneBack = 0.12f;
        private const int Segments = 24;

        // The runes' place on the face (u across the face's width, v up its box), and a cell per rune.
        private const float RuneTop = 0.92f, RuneCell = 0.095f, GrooveWidth = 0.008f;

        [MenuItem("Mjolnir/Examples/Make the Altar")]
        public static void Make()
        {
            System.IO.Directory.CreateDirectory(Folder);
            var stone = ModelParts.Material(Folder, "Altar_Stone", ModelParts.Texture(Folder, "Altar_Stone", Stone, 512), 0.12f);
            var face = ModelParts.Material(Folder, "Altar_Runes", ModelParts.Texture(Folder, "Altar_Runes", Runes, 1024), 0.12f);
            ModelParts.Prefab(Folder, "Altar", Mesh(), new[] { stone, face }, solid: true);
            Debug.Log($"Mjolnir kit: the altar made in {Folder}");
        }

        private static Mesh Mesh()
        {
            var mesh = new ModelParts.Builder();
            mesh.Part(1); // the runes' part exists even before its face is added
            // The slab on the ground.
            Box(mesh, new Vector3(0, SlabHeight / 2, 0), new Vector3(SlabWidth, SlabHeight, SlabDepth), runesFront: false);
            // The runestone on its back half, its front face carrying the runes.
            float z = -SlabDepth / 2 + StoneBack + StoneDepth / 2;
            float boxHeight = StoneHeight - StoneWidth / 2;
            Box(mesh, new Vector3(0, SlabHeight + boxHeight / 2, z), new Vector3(StoneWidth, boxHeight, StoneDepth), runesFront: true);
            RoundTop(mesh, new Vector3(0, SlabHeight + boxHeight, z), StoneWidth / 2, StoneDepth);
            return mesh.Build("Altar_Mesh");
        }

        // A box; its +Z face takes the runes' texture whole when asked, the others the stone's, a metre of texture a metre.
        private static void Box(ModelParts.Builder mesh, Vector3 centre, Vector3 size, bool runesFront)
        {
            var half = size / 2;
            Face(mesh, runesFront ? 1 : 0, centre + new Vector3(0, 0, half.z), Vector3.forward, Vector3.up, size.x, size.y, whole: runesFront);
            Face(mesh, 0, centre - new Vector3(0, 0, half.z), Vector3.back, Vector3.up, size.x, size.y);
            Face(mesh, 0, centre + new Vector3(half.x, 0, 0), Vector3.right, Vector3.up, size.z, size.y);
            Face(mesh, 0, centre - new Vector3(half.x, 0, 0), Vector3.left, Vector3.up, size.z, size.y);
            Face(mesh, 0, centre + new Vector3(0, half.y, 0), Vector3.up, Vector3.forward, size.x, size.z);
            Face(mesh, 0, centre - new Vector3(0, half.y, 0), Vector3.down, Vector3.forward, size.x, size.z);
        }

        // A rectangle facing outward, up being its up: its texture whole (0..1) or a metre a metre.
        private static void Face(ModelParts.Builder mesh, int part, Vector3 centre, Vector3 outward, Vector3 up, float width, float height, bool whole = false)
        {
            var right = Vector3.Cross(up, -outward); // seen from outside
            var h = right * (width / 2);
            var v = up * (height / 2);
            float su = whole ? 1 : width, sv = whole ? 1 : height;
            float ou = whole ? 0 : centre.x + centre.z, ov = whole ? 0 : centre.y;
            int start = mesh.Vertices.Count;
            Add(mesh, centre - h - v, outward, new Vector2(ou, ov));
            Add(mesh, centre - h + v, outward, new Vector2(ou, ov + sv));
            Add(mesh, centre + h + v, outward, new Vector2(ou + su, ov + sv));
            Add(mesh, centre + h - v, outward, new Vector2(ou + su, ov));
            Quad(mesh, part, start, start + 1, start + 2, start + 3, outward);
        }

        // The runestone's top: half a cylinder along Z over the box, and its two half-disc ends.
        private static void RoundTop(ModelParts.Builder mesh, Vector3 centre, float radius, float depth)
        {
            float front = centre.z + depth / 2, back = centre.z - depth / 2;
            int start = mesh.Vertices.Count;
            for (int i = 0; i <= Segments; i++)
            {
                float a = Mathf.PI * i / Segments;
                var outward = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                float u = a * radius;
                Add(mesh, new Vector3(centre.x, centre.y, back) + outward * radius, outward, new Vector2(u, 0));
                Add(mesh, new Vector3(centre.x, centre.y, front) + outward * radius, outward, new Vector2(u, depth));
            }
            for (int i = 0; i < Segments; i++)
            {
                int a = start + i * 2;
                Quad(mesh, 0, a, a + 1, a + 3, a + 2, ArcNormal(i, radius));
            }
            foreach (var (z, facing) in new[] { (front, Vector3.forward), (back, Vector3.back) })
            {
                int middle = mesh.Vertices.Count;
                Add(mesh, new Vector3(centre.x, centre.y, z), facing, new Vector2(0, centre.y));
                for (int i = 0; i <= Segments; i++)
                {
                    float a = Mathf.PI * i / Segments;
                    var p = new Vector3(centre.x + Mathf.Cos(a) * radius, centre.y + Mathf.Sin(a) * radius, z);
                    Add(mesh, p, facing, new Vector2(p.x, p.y));
                }
                for (int i = 0; i < Segments; i++) Triangle(mesh, 0, middle, middle + 1 + i, middle + 2 + i, facing);
            }
        }

        private static Vector3 ArcNormal(int i, float radius)
        {
            float a = Mathf.PI * (i + 0.5f) / Segments;
            return new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
        }

        private static void Add(ModelParts.Builder mesh, Vector3 position, Vector3 normal, Vector2 uv)
        {
            mesh.Vertices.Add(position);
            mesh.Normals.Add(normal);
            mesh.Uvs.Add(uv);
        }

        private static void Quad(ModelParts.Builder mesh, int part, int a, int b, int c, int d, Vector3 outward)
        {
            Triangle(mesh, part, a, b, c, outward);
            Triangle(mesh, part, a, c, d, outward);
        }

        // A triangle that faces outward: Unity draws a triangle clockwise to the eye, whose normal Cross(b - a, c - a) points
        // to the eye.
        private static void Triangle(ModelParts.Builder mesh, int part, int a, int b, int c, Vector3 outward)
        {
            var v = mesh.Vertices;
            if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), outward) < 0) (b, c) = (c, b);
            mesh.Part(part).AddRange(new[] { a, b, c });
        }

        // Grey granite with darker flecks and a greenish lichen here and there.
        private static Color Stone(float u, float v)
        {
            float grain = Mathf.PerlinNoise(u * 9, v * 9) * 0.5f + Mathf.PerlinNoise(u * 31, v * 31) * 0.3f + Mathf.PerlinNoise(u * 97, v * 97) * 0.2f;
            float fleck = Mathf.PerlinNoise(u * 211 + 7, v * 211 + 3) > 0.72f ? 0.12f : 0f;
            float lichen = Mathf.Clamp01((Mathf.PerlinNoise(u * 4 + 11, v * 4 + 5) - 0.62f) * 4f);
            var stone = Color.Lerp(new Color(0.30f, 0.30f, 0.29f), new Color(0.52f, 0.51f, 0.48f), grain) * (1f - fleck);
            return Color.Lerp(stone, new Color(0.36f, 0.40f, 0.28f), lichen * 0.6f);
        }

        // The runestone's face: its stone, a carved border, and a column of runes cut into it and painted red.
        private static Color Runes(float u, float v)
        {
            var stone = Stone(u * StoneWidth, v * (StoneHeight - StoneWidth / 2));
            // In metres on the face.
            float x = u * StoneWidth, y = v * (StoneHeight - StoneWidth / 2), w = StoneWidth, hgt = StoneHeight - StoneWidth / 2;
            float border = Mathf.Min(Mathf.Abs(x - 0.04f), Mathf.Abs(w - 0.04f - x));
            bool inBorder = border < GrooveWidth && y > 0.04f;
            float d = inBorder ? 0f : float.MaxValue;
            if (!inBorder) d = Mathf.Min(d, RunesDistance(x - w / 2, y - hgt * RuneTop));
            if (d >= GrooveWidth) return stone;
            // The groove: darker towards its middle, the paint worn in places.
            float depth = 1f - d / GrooveWidth;
            float worn = Mathf.PerlinNoise(x * 60, y * 60);
            var paint = new Color(0.42f, 0.09f, 0.06f);
            var groove = Color.Lerp(stone * 0.55f, paint, worn > 0.35f ? 0.85f : 0.3f);
            return Color.Lerp(stone, groove, Mathf.SmoothStep(0, 1, depth * 1.5f));
        }

        // Six runes, one under the other from the top: the distance in metres from (x, y) (x from the face's middle, y down
        // from the first rune's top, negative below) to the nearest stroke.
        private static float RunesDistance(float x, float y)
        {
            float best = float.MaxValue;
            for (int i = 0; i < Glyphs.Length; i++)
            {
                // The cell's corner: the rune RuneCell high, centred across.
                float top = -i * (RuneCell + 0.02f);
                float cx = (x + RuneCell * 0.4f) / RuneCell, cy = (y - top + RuneCell) / RuneCell;
                if (cx < -0.3f || cx > 1.3f || cy < -0.3f || cy > 1.3f) continue;
                foreach (var (a, b) in Glyphs[i])
                    best = Mathf.Min(best, Segment(new Vector2(cx, cy), a, b) * RuneCell);
            }
            return best;
        }

        private static float Segment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        // Elder Futhark runes as strokes in a cell 0.8 wide and 1 high: fehu, uruz, thurisaz, ansuz, raido, tiwaz.
        private static readonly (Vector2, Vector2)[][] Glyphs =
        {
            new[] { (V(0.2f, 0), V(0.2f, 1)), (V(0.2f, 0.62f), V(0.62f, 0.9f)), (V(0.2f, 0.36f), V(0.62f, 0.64f)) },
            new[] { (V(0.2f, 0), V(0.2f, 1)), (V(0.2f, 1), V(0.6f, 0.72f)), (V(0.6f, 0.72f), V(0.6f, 0)) },
            new[] { (V(0.2f, 0), V(0.2f, 1)), (V(0.2f, 0.75f), V(0.55f, 0.5f)), (V(0.55f, 0.5f), V(0.2f, 0.25f)) },
            new[] { (V(0.2f, 0), V(0.2f, 1)), (V(0.2f, 1), V(0.6f, 0.76f)), (V(0.2f, 0.74f), V(0.6f, 0.5f)) },
            new[] { (V(0.2f, 0), V(0.2f, 1)), (V(0.2f, 1), V(0.58f, 0.76f)), (V(0.58f, 0.76f), V(0.2f, 0.52f)), (V(0.2f, 0.52f), V(0.6f, 0)) },
            new[] { (V(0.4f, 0), V(0.4f, 1)), (V(0.4f, 1), V(0.1f, 0.72f)), (V(0.4f, 1), V(0.7f, 0.72f)) },
        };

        private static Vector2 V(float x, float y) => new(x, y);
    }
}
