using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AsmResolver.DotNet;

namespace Mjolnir.CodeMap;

/// <summary>A game method with its compiled code location, from Cpp2IL's [Address] attribute.</summary>
internal sealed record MethodEntry(string Name, string Type, string Assembly, ulong Rva, long FileOffset, int Length, bool IsVirtual, bool IsStatic)
{
    public bool Contains(ulong rva) => rva >= Rva && rva < Rva + (ulong)Length;
}

/// <summary>A type's instance field offsets (Cpp2IL's [FieldOffset]) and its base type, for annotating disassembly.</summary>
internal sealed record TypeEntry(string Name, string? BaseType, IReadOnlyDictionary<int, string> Fields);

/// <summary>
/// Method addresses and field offsets of the whole game, read from the Cpp2IL dummy assemblies
/// (tools/gen-interop.ps1 -DummyDir build/interop-dummy). Names only, never hardcoded into the loader.
/// </summary>
internal sealed class CodeIndex
{
    private CodeIndex(List<MethodEntry> methods, Dictionary<string, TypeEntry> types)
    {
        Methods = methods;
        Types = types;
        ByRva = methods.GroupBy(m => m.Rva).ToDictionary(g => g.Key, g => g.ToList());
    }

    public IReadOnlyList<MethodEntry> Methods { get; }

    /// <summary>Several methods can share one body (identical code folding by the linker).</summary>
    public IReadOnlyDictionary<ulong, List<MethodEntry>> ByRva { get; }

    public IReadOnlyDictionary<string, TypeEntry> Types { get; }

    public static CodeIndex Load(string dummyDir)
    {
        var methods = new List<MethodEntry>();
        var types = new Dictionary<string, TypeEntry>(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(dummyDir, "*.dll").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            string asm = Path.GetFileNameWithoutExtension(file);
            foreach (var type in ModuleDefinition.FromFile(file).GetAllTypes())
            {
                string typeName = type.FullName;
                var fields = new Dictionary<int, string>();
                foreach (var field in type.Fields.Where(f => !f.IsStatic))
                {
                    if (Attribute(field.CustomAttributes, "FieldOffsetAttribute", "Offset") is { } offset)
                        fields.TryAdd((int)offset, field.Name!);
                }
                types.TryAdd(typeName, new TypeEntry(typeName, type.BaseType?.FullName, fields));

                foreach (var method in type.Methods)
                {
                    if (Attribute(method.CustomAttributes, "AddressAttribute", "RVA") is not { } rva) continue;
                    long offset = (long)(Attribute(method.CustomAttributes, "AddressAttribute", "Offset") ?? 0);
                    int length = (int)(Attribute(method.CustomAttributes, "AddressAttribute", "Length") ?? 0);
                    string parameters = string.Join(", ", method.Parameters.Select(p => ShortName(p.ParameterType.FullName)));
                    methods.Add(new MethodEntry($"{typeName}::{method.Name}({parameters})", typeName, asm, rva, offset, length,
                                                method.IsVirtual, method.IsStatic));
                }
            }
        }
        return new CodeIndex(methods, types);
    }

    /// <summary>The same index with method lengths taken from the image's own function bounds where it has them.</summary>
    public CodeIndex WithBounds(IReadOnlyDictionary<ulong, ulong> functionEnds) =>
        new([.. Methods.Select(m => functionEnds.TryGetValue(m.Rva, out ulong end) ? m with { Length = (int)(end - m.Rva) } : m)],
            new Dictionary<string, TypeEntry>(Types, StringComparer.Ordinal));

    /// <summary>Instance fields of <paramref name="typeName"/> and its base types, by offset.</summary>
    public IEnumerable<(string Type, string Field)> FieldsAt(string typeName, int offset)
    {
        for (string? name = typeName; name != null && Types.TryGetValue(name, out var type); name = type.BaseType)
        {
            if (type.Fields.TryGetValue(offset, out var field)) yield return (type.Name, field);
        }
    }

    private static ulong? Attribute(IList<CustomAttribute> attributes, string attributeName, string argument)
    {
        foreach (var attribute in attributes)
        {
            if (attribute.Constructor?.DeclaringType?.Name != attributeName) continue;
            var named = attribute.Signature?.NamedArguments.FirstOrDefault(a => a.MemberName == argument);
            // AsmResolver returns string arguments as Utf8String, hence ToString().
            if (named?.Argument.Element?.ToString() is { } text && text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                ulong.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong value))
                return value;
        }
        return null;
    }

    private static string ShortName(string fullName)
    {
        int generic = fullName.IndexOf('<');
        string head = generic < 0 ? fullName : fullName[..generic];
        int dot = head.LastIndexOf('.');
        return dot < 0 ? fullName : fullName[(dot + 1)..];
    }
}
