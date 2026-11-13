using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Mjolnir.SDK.Config;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.Animation;

/// <summary>
/// The game's own animation clips for the Mjolnir kit: when the platform's
/// setting <c>[Kit] Animations</c> is on, every clip of the game that moves the people's skeleton goes to
/// <c>Mjolnir/ModKit/clips/</c>, a file per clip and <c>clips.txt</c> listing them, which the kit's Import Rigs turns into clips
/// to preview and to look at. They stay on the player's computer: the kit keeps them out of git and out of mods' bundles.
/// <para>
/// The game keeps no way to read a clip's curves (no <c>SampleAnimation</c>, no bindings), so each clip is played on a bare copy
/// of the people's skeleton (every bone of the four bodies, no mesh, no controller) with an Animator of its own, through a
/// playable graph evaluated by hand, and read back frame by frame at the clip's own rate. Before that every bone is set to a
/// mark: what the clip leaves at the mark it does not animate, so a clip keeps only its own curves (the bones it lacks stay
/// the game's, as in the game). Its root motion is `master`'s own movement, as the game's avatars take it.
/// </para>
/// The work is spread over frames (8 ms a frame), in the main menu once the skeletons are written, and done again only when
/// the game's clips are other ones (their names, lengths and rates).
/// </summary>
internal static class GameClips
{
    /// <summary>The platform's setting, bound by the module's start.</summary>
    public static ConfigEntry<bool>? Setting;

    public static bool Enabled => Setting?.Value == true;

    private const int Format = 1;
    private const int MinimumBones = 10;     // a clip of the people moves their Biped, not a wolf's or a door's
    private const double BudgetMs = 8;       // a frame's share of the work
    private const float Same = 0.00001f;     // a value that does not change through the clip is written once
    private static readonly Vector3 MarkPosition = new(1234f, 1234f, 1234f);
    private static readonly Quaternion MarkRotation = new(0.1f, 0.2f, 0.3f, 0.927362f);
    private static readonly Vector3 MarkScale = new(7f, 7f, 7f);

    private static Work? s_work;
    private static bool s_done;

    /// <summary>Writes the clips again at the next look (the setting was turned on).</summary>
    public static void Again()
    {
        s_done = false;
        s_work?.Dispose();
        s_work = null;
    }

    /// <summary>Every frame: starts the work once the skeletons are written, then goes on with it within its budget.</summary>
    public static void Tick()
    {
        if (!Enabled || s_done || KitExport.Clips is not { } clips) return;
        if (s_work == null && Rigs.Skeletons.Count == 0) return; // not written yet (the main menu brings the bodies)
        try
        {
            if (s_work == null)
            {
                s_work = Work.Begin(clips);
                if (s_work == null)
                {
                    s_done = true;
                    return;
                }
            }
            if (s_work.Step(BudgetMs)) return;
        }
        catch (Exception e)
        {
            // Not tried again every frame: the next start, or the setting turned on again, tries again.
            SdkLog.Error(GameAnimations.Source, $"Animations: the game's clips were not written for the Mjolnir kit, not tried again until the next start: {e}");
        }
        s_work?.Dispose();
        s_work = null;
        s_done = true;
    }

    private sealed class Work : IDisposable
    {
        private readonly string _folder;
        private readonly string _manifest;
        private readonly Queue<Item> _todo;
        private readonly List<string> _signed = []; // the stamps of the clips looked at to the end: written, or not the people's
        private readonly Transform _root;
        private readonly List<(string Path, Transform Bone)> _bones;
        private readonly Animator _animator;
        private readonly Stopwatch _watch = Stopwatch.StartNew();
        private readonly StringBuilder _list = new();
        private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);
        private int _written, _skipped;
        private long _bytes;

        private Work(string folder, string manifest, Queue<Item> todo, Transform root, List<(string, Transform)> bones, Animator animator)
        {
            _folder = folder;
            _manifest = manifest;
            _todo = todo;
            _root = root;
            _bones = bones;
            _animator = animator;
        }

        // The clips to read, or null when the folder already has them.
        public static Work? Begin(string folder)
        {
            var clips = Resources.FindObjectsOfTypeAll<AnimationClip>()
                                 .Where(c => c != null && !c.empty && c.length > 0 && !c.legacy)
                                 .OrderBy(c => c.name, StringComparer.Ordinal).ThenBy(c => c.length).ToList();
            // A name twice (the game has a few) gets a number, so each clip has a file of its own.
            var named = new List<Item>();
            var taken = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var clip in clips)
            {
                string name = clip.name.Length == 0 ? "unnamed" : clip.name;
                int n = taken.TryGetValue(name, out int count) ? count + 1 : 1;
                taken[name] = n;
                name = n == 1 ? name : $"{name} ({n})";
                named.Add(new Item(clip, name, string.Create(CultureInfo.InvariantCulture, $"{name}|{clip.length:R}|{clip.frameRate:R};")));
            }
            string manifestPath = Path.Combine(folder, "clips.txt");
            if (File.Exists(manifestPath) && File.ReadAllText(manifestPath).StartsWith(Header(named.Select(i => i.Stamp).ToList()), StringComparison.Ordinal))
                return null;

            // The folder first: a failure there leaves nothing made in the world.
            Directory.CreateDirectory(folder);
            foreach (string old in Directory.GetFiles(folder)) File.Delete(old);

            var skeleton = Union(Rigs.Skeletons);
            var root = new GameObject("Mjolnir clips").transform;
            try
            {
                root.gameObject.hideFlags = HideFlags.HideAndDontSave;
                Object.DontDestroyOnLoad(root.gameObject);
                root.position = new Vector3(0, -1000, 0);
                var byPath = new Dictionary<string, Transform>(StringComparer.Ordinal) { [""] = root };
                var bones = new List<(string, Transform)>();
                foreach (var (path, position, rotation) in skeleton)
                {
                    int slash = path.LastIndexOf('/');
                    if (!byPath.TryGetValue(slash < 0 ? "" : path[..slash], out var parent)) continue;
                    var bone = new GameObject(path[(slash + 1)..]).transform;
                    bone.SetParent(parent, false);
                    bone.localPosition = position;
                    bone.localRotation = rotation;
                    byPath[path] = bone;
                    bones.Add((path, bone));
                }
                var animator = root.gameObject.AddComponent<Animator>();
                animator.applyRootMotion = false; // master's own movement is the root motion, as the game's avatars read it
                SdkLog.Info(GameAnimations.Source, $"Animations: writing the game's clips for the Mjolnir kit to {folder} ({named.Count} to look at, on a skeleton of {bones.Count} bones)");
                return new Work(folder, manifestPath, new Queue<Item>(named), root, bones, animator);
            }
            catch
            {
                Object.Destroy(root.gameObject);
                throw;
            }
        }

        // True while clips are left.
        public bool Step(double budgetMs)
        {
            var frame = Stopwatch.StartNew();
            while (_todo.Count > 0 && frame.Elapsed.TotalMilliseconds < budgetMs)
            {
                var (clip, name, stamp) = _todo.Dequeue();
                if (clip == null) continue; // unloaded since: not signed, so the next start looks at the clips again
                try
                {
                    if (Read(clip, name) is { } line)
                    {
                        _list.Append(line);
                        _written++;
                    }
                    else _skipped++;
                    _signed.Add(stamp);
                }
                catch (Exception e)
                {
                    _skipped++;
                    SdkLog.Warn(GameAnimations.Source, $"Animations: the game's clip {name} could not be read for the kit: {e.Message}");
                }
            }
            if (_todo.Count > 0) return true;
            // Last: the kit reads a folder whose clips.txt is there. Its header signs only the clips looked at to the end, so a
            // clip that failed or went makes the next start write them again.
            File.WriteAllText(_manifest, Header(_signed) + _list);
            SdkLog.Info(GameAnimations.Source, string.Create(CultureInfo.InvariantCulture,
                $"Animations: {_written} of the game's clips written for the Mjolnir kit ({_bytes / 1048576.0:0.0} MB, {_watch.Elapsed.TotalSeconds:0.0} s); {_skipped} left out (not the people's)"));
            return false;
        }

        // One clip into its file; its line for clips.txt, null for a clip that does not move the people.
        private string? Read(AnimationClip clip, string name)
        {
            var graph = PlayableGraph.Create("Mjolnir clip");
            try
            {
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var clipHandle = AnimationClipPlayable.Create(graph, clip).GetHandle();
                AnimationPlayableOutput.Create(graph, "Mjolnir", _animator).GetHandle().SetSourcePlayable(clipHandle, 0);
                graph.Play();

                // What the clip writes: whatever leaves the mark, at its middle.
                foreach (var (_, bone) in _bones)
                {
                    bone.localPosition = MarkPosition;
                    bone.localRotation = MarkRotation;
                    bone.localScale = MarkScale;
                }
                clipHandle.SetTime(clip.length * 0.5);
                graph.Evaluate(0f);
                var channels = new List<Channel>();
                int moved = 0;
                foreach (var (path, bone) in _bones)
                {
                    bool position = (bone.localPosition - MarkPosition).sqrMagnitude > 1e-6f;
                    bool rotation = Quaternion.Angle(bone.localRotation, MarkRotation) > 0.001f;
                    bool scale = (bone.localScale - MarkScale).sqrMagnitude > 1e-6f;
                    if (position) channels.Add(new Channel(path, bone, 0));
                    if (rotation) channels.Add(new Channel(path, bone, 1));
                    if (scale) channels.Add(new Channel(path, bone, 2));
                    if ((position || rotation) && path.StartsWith("master/Bip001", StringComparison.Ordinal)) moved++;
                }
                if (moved < MinimumBones) return null;

                float rate = clip.frameRate > 0 ? clip.frameRate : 30f;
                int frames = Math.Max(1, (int)Math.Round(clip.length * rate)) + 1;
                for (int f = 0; f < frames; f++)
                {
                    clipHandle.SetTime(Math.Min(f / (double)rate, clip.length));
                    graph.Evaluate(0f);
                    foreach (var channel in channels) channel.Take();
                }
                byte[] data = Bytes(name, clip, rate, frames, channels);
                string file = KitFiles.Safe(name) + ".clip";
                for (int n = 2; !_files.Add(file); n++) file = $"{KitFiles.Safe(name)}_{n}.clip";
                File.WriteAllBytes(Path.Combine(_folder, file), data);
                _bytes += data.Length;
                return string.Create(CultureInfo.InvariantCulture,
                    $"{name}|{file}|{clip.length:R}|{rate:R}|{(clip.isLooping ? 1 : 0)}|{frames}|{channels.Count}|{MadeFor(name)}\n");
            }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
            }
        }

        public void Dispose()
        {
            if (_root != null) Object.Destroy(_root.gameObject);
        }
    }

    // A property of a bone through the clip: its local position (0), rotation (1) or scale (2), frame by frame.
    private sealed class Channel
    {
        public readonly string Path;
        public readonly byte Kind;
        public readonly List<Vector4> Values = new();
        private readonly Transform _bone;

        public Channel(string path, Transform bone, byte kind)
        {
            Path = path;
            _bone = bone;
            Kind = kind;
        }

        public void Take()
        {
            switch (Kind)
            {
                case 0: Values.Add(_bone.localPosition); break;
                case 2: Values.Add(_bone.localScale); break;
                default:
                    var q = _bone.localRotation;
                    var v = new Vector4(q.x, q.y, q.z, q.w);
                    // The shorter way from the frame before, so the kit's curves turn the way the clip does.
                    if (Values.Count > 0 && Vector4.Dot(Values[^1], v) < 0) v = -v;
                    Values.Add(v);
                    break;
            }
        }
    }

    // "MJC1": the clip's name, length, rate, whether it loops, its frame count and its curves: each the bone's path, the kind,
    // whether it stays one value, then that value or one per frame (3 numbers, 4 for a rotation).
    private static byte[] Bytes(string name, AnimationClip clip, float rate, int frames, List<Channel> channels)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("MJC1"));
        writer.Write(name);
        writer.Write(clip.length);
        writer.Write(rate);
        writer.Write(clip.isLooping);
        writer.Write(frames);
        writer.Write(channels.Count);
        foreach (var channel in channels)
        {
            int size = channel.Kind == 1 ? 4 : 3;
            bool constant = channel.Values.All(v => (v - channel.Values[0]).sqrMagnitude < Same * Same);
            writer.Write(channel.Path);
            writer.Write(channel.Kind);
            writer.Write(constant);
            foreach (var v in constant ? channel.Values.Take(1) : channel.Values)
                for (int i = 0; i < size; i++) writer.Write(v[i]);
        }
        writer.Flush();
        return stream.ToArray();
    }

    // The people's bones of every body in one skeleton: a path the first body has keeps its pose there (the player's woman
    // first, the fullest), so every clip finds the bones it names.
    private static List<(string Path, Vector3 Position, Quaternion Rotation)> Union(IReadOnlyList<Rigs.Skeleton> skeletons)
    {
        var union = new List<(string, Vector3, Quaternion)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var skeleton in skeletons.OrderBy(s => s.Name == "aska" ? 0 : s.Name == "ragnar" ? 1 : 2).ThenBy(s => s.Name, StringComparer.Ordinal))
            foreach (var bone in skeleton.Bones)
                if (seen.Add(bone.Path)) union.Add(bone);
        return union;
    }

    // A clip to look at: its file's name and its stamp for the signature (its name, length and rate).
    private readonly record struct Item(AnimationClip Clip, string Name, string Stamp);

    private static string Header(IReadOnlyList<string> stamps) =>
        string.Create(CultureInfo.InvariantCulture,
            $"# Mjolnir game clips, format {Format}: the game's own clips of the people, written by Mjolnir.SDK.Animation for the Mjolnir kit; only for this computer\n") +
        string.Create(CultureInfo.InvariantCulture, $"# of the game's {stamps.Count} clips, {Signature(stamps)}\n") +
        "# clip|file|length (s)|frames per second|loops|frames|curves|made for\n";

    // What the clips are: their names, lengths and rates, so a game with other clips writes them again.
    private static string Signature(IEnumerable<string> stamps)
    {
        ulong hash = 14695981039346656037;
        foreach (string stamp in stamps)
            foreach (char c in stamp)
            {
                hash ^= c;
                hash *= 1099511628211;
            }
        return string.Create(CultureInfo.InvariantCulture, $"signature {hash:x16}");
    }

    // The skeleton a clip was made for, by the game's names: the kit's folder for it.
    private static string MadeFor(string name)
    {
        foreach (string rig in new[] { "peasant_female", "peasant_male", "ragnar", "aska" })
            if (name.StartsWith(rig + "_", StringComparison.OrdinalIgnoreCase)) return rig;
        return "";
    }
}
