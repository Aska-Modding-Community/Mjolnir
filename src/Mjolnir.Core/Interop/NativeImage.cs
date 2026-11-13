using System.Collections.Generic;
using System.Text;

namespace Mjolnir.Core.Interop;

/// <summary>
/// Section map of a loaded PE image, read from its in-memory headers. Used to tell IL2CPP runtime code
/// (GameAssembly's ".text") from generated game code (its "il2cpp" section). Sections are found by
/// name, never by address.
/// </summary>
internal sealed unsafe class NativeImage
{
    private readonly List<(string Name, nint Start, nint End)> _sections = new();

    public NativeImage(nint module)
    {
        Base = module;
        byte* b = (byte*)module;
        byte* nt = b + *(int*)(b + 0x3C);
        ushort count = *(ushort*)(nt + 6);
        ushort optionalHeaderSize = *(ushort*)(nt + 20);
        byte* section = nt + 24 + optionalHeaderSize;
        for (int i = 0; i < count; i++, section += 40)
        {
            string name = Encoding.ASCII.GetString(section, 8).TrimEnd('\0');
            uint virtualSize = *(uint*)(section + 8);
            uint virtualAddress = *(uint*)(section + 12);
            _sections.Add((name, module + (nint)virtualAddress, module + (nint)virtualAddress + (nint)virtualSize));
        }
    }

    public nint Base { get; }

    /// <summary>Name of the section containing <paramref name="address"/>, or null if it is outside the image.</summary>
    public string? SectionOf(nint address)
    {
        foreach (var (name, start, end) in _sections)
            if (address >= start && address < end) return name;
        return null;
    }
}
