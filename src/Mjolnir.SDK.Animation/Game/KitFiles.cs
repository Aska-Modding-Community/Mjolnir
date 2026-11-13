using System.Linq;
using UnityEngine;

namespace Mjolnir.SDK.Animation;

/// <summary>What the files written for the Mjolnir kit share: their names, and the paths of the bones in them.</summary>
internal static class KitFiles
{
    /// <summary>A name the file system takes: letters, digits, <c>_</c>, <c>-</c> and <c>.</c> kept, the rest <c>_</c>.</summary>
    public static string Safe(string name) =>
        new(name.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.' ? c : '_').ToArray());

    /// <summary>The path of <paramref name="t"/> under <paramref name="root"/>, by its objects' names (<c>master/Bip001/…</c>).</summary>
    public static string PathOf(Transform root, Transform t)
    {
        string path = t.name;
        for (var p = t.parent; p != null && p != root; p = p.parent) path = p.name + "/" + path;
        return path;
    }
}
