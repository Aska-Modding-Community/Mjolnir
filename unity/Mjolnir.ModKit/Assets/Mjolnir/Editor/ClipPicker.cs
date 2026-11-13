using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Mjolnir.ModKit
{
    /// <summary>
    /// A clip to preview, searched for by name: the mods' clips (under Assets/Mods, by mod and bundle, clips inside model files
    /// too) and the game's own (by the skeleton the game made them for). The list holds names only: a game clip is made when
    /// it is chosen (its file), a mod's loaded then. Each item carries its own clip (the dropdown renumbers items' ids, so
    /// neither folders nor choices go by them: a folder was made again for every clip, and a choice chose nothing).
    /// </summary>
    internal sealed class ClipPicker : AdvancedDropdown
    {
        private readonly System.Action<AnimationClip> _chosen;

        private sealed class ClipItem : AdvancedDropdownItem
        {
            public readonly System.Func<AnimationClip> Load;

            public ClipItem(string name, System.Func<AnimationClip> load) : base(name) => Load = load;
        }

        public ClipPicker(System.Action<AnimationClip> chosen) : base(new AdvancedDropdownState())
        {
            _chosen = chosen;
            minimumSize = new Vector2(360, 460);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem("Clips");
            var folders = new Dictionary<string, AdvancedDropdownItem>(System.StringComparer.Ordinal);
            AdvancedDropdownItem Folder(AdvancedDropdownItem parent, string key, string path)
            {
                foreach (string part in path.Split(new[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries))
                {
                    key += "/" + part;
                    if (!folders.TryGetValue(key, out var folder))
                    {
                        folder = new AdvancedDropdownItem(part);
                        parent.AddChild(folder);
                        folders[key] = folder;
                    }
                    parent = folder;
                }
                return parent;
            }

            var mods = new AdvancedDropdownItem("Mods' clips");
            foreach (string path in AssetDatabase.FindAssets("t:AnimationClip", new[] { BundleBuilder.ModsFolder }).Select(AssetDatabase.GUIDToAssetPath).Distinct().OrderBy(p => p, System.StringComparer.Ordinal))
            {
                string folder = System.IO.Path.GetDirectoryName(path.Substring(BundleBuilder.ModsFolder.Length + 1)).Replace('\\', '/');
                if (path.EndsWith(".anim", System.StringComparison.OrdinalIgnoreCase))
                {
                    string clipPath = path;
                    Folder(mods, "mods", folder).AddChild(new ClipItem(System.IO.Path.GetFileNameWithoutExtension(path), () => AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath)));
                    continue;
                }
                // A model file's clips (a few files, small): their names are inside it.
                foreach (var clip in KitCommon.ClipsIn(path))
                {
                    var found = clip;
                    Folder(mods, "mods", folder).AddChild(new ClipItem(clip.name, () => found));
                }
            }
            if (!mods.children.Any()) mods.AddChild(new AdvancedDropdownItem("none yet: a mod's clips go under Assets/Mods/<mod>/<bundle>") { enabled = false });
            root.AddChild(mods);

            var game = new AdvancedDropdownItem("The game's clips");
            foreach (var entry in GameClips.Entries())
            {
                var e = entry;
                Folder(game, "game", entry.Group).AddChild(new ClipItem(entry.Name, () => GameClips.Load(e)));
            }
            if (!game.children.Any()) game.AddChild(new AdvancedDropdownItem("not imported: turn on \"The game's animations for the kit\" in the game, then Import Rigs") { enabled = false });
            root.AddChild(game);
            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item is ClipItem clipItem && clipItem.Load() is { } clip) _chosen(clip);
        }
    }
}
