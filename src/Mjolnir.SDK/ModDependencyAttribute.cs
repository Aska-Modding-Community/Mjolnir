using System;
using Mjolnir.Core.Mods;

namespace Mjolnir.SDK;

/// <summary>
/// Another mod this one needs; it is loaded first. If it is missing, failed to load or its version is outside
/// <paramref name="versionRange"/>, this mod is not loaded. An <see cref="Optional"/> dependency may be missing;
/// if present, it still loads first and must match the range.
/// <para>Range syntax: bounds separated by spaces or commas, all must hold — <c>&gt;=</c>, <c>&gt;</c>, <c>&lt;=</c>,
/// <c>&lt;</c>, <c>=</c>; a bare version is a minimum; <c>*</c> or empty accepts any version.
/// Example: <c>"&gt;=1.2 &lt;2.0"</c>.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ModDependencyAttribute(string dependencyId, string versionRange = "*") : Attribute, IModDependencyDeclaration
{
    public string DependencyId { get; } = dependencyId ?? "";
    public string VersionRange { get; } = versionRange ?? "*";
    public bool Optional { get; set; }

    string IModDependencyDeclaration.Id => DependencyId;
}
