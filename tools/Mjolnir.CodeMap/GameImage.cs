using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Mjolnir.CodeMap;

/// <summary>GameAssembly.dll read from disk: sections and exported il2cpp_* functions (the only native names it has).</summary>
internal sealed class GameImage
{
    private readonly List<(string Name, uint Va, uint Size, uint Raw)> _sections = [];

    public GameImage(string path)
    {
        Bytes = File.ReadAllBytes(path);
        int nt = BitConverter.ToInt32(Bytes, 0x3C);
        int sectionCount = BitConverter.ToUInt16(Bytes, nt + 6);
        int optionalSize = BitConverter.ToUInt16(Bytes, nt + 20);
        int optional = nt + 24;
        int header = optional + optionalSize;
        for (int i = 0; i < sectionCount; i++, header += 40)
        {
            string name = Encoding.ASCII.GetString(Bytes, header, 8).TrimEnd('\0');
            _sections.Add((name, BitConverter.ToUInt32(Bytes, header + 12), BitConverter.ToUInt32(Bytes, header + 8),
                           BitConverter.ToUInt32(Bytes, header + 20)));
        }

        // Data directory 0 = exports; PE32+ optional header puts data directories at +112.
        uint exportRva = BitConverter.ToUInt32(Bytes, optional + 112);
        if (exportRva != 0) ReadExports(exportRva);

        // Data directory 3 = exception table (.pdata): x64 RUNTIME_FUNCTION {Begin, End, UnwindInfo} of every
        // non-leaf function, the compiler's own function bounds.
        uint pdataRva = BitConverter.ToUInt32(Bytes, optional + 112 + 3 * 8);
        uint pdataSize = BitConverter.ToUInt32(Bytes, optional + 112 + 3 * 8 + 4);
        if (pdataRva != 0) ReadFunctions(pdataRva, pdataSize);
    }

    public byte[] Bytes { get; }

    public Dictionary<ulong, string> Exports { get; } = [];

    /// <summary>
    /// End RVA of each function starting at the key, from .pdata (chained pieces folded in). Cpp2IL's method
    /// Length stops early on some bodies (at a switch jump table, e.g. coroutine MoveNext), which hid call sites.
    /// Leaf functions have no entry and keep Cpp2IL's length.
    /// </summary>
    public Dictionary<ulong, ulong> FunctionEnds { get; } = [];

    public string? SectionOf(ulong rva) =>
        _sections.FirstOrDefault(s => rva >= s.Va && rva < (ulong)s.Va + s.Size).Name;

    public long FileOffset(ulong rva)
    {
        var s = _sections.First(s => rva >= s.Va && rva < (ulong)s.Va + s.Size);
        return (long)(rva - s.Va + s.Raw);
    }

    private void ReadFunctions(uint rva, uint size)
    {
        // A big function is often split into several entries: the first, then "chained" ones (UNW_FLAG_CHAININFO)
        // whose unwind info points back to the entry they continue. Fold contiguous chains into their root.
        var parent = new Dictionary<uint, uint>();
        var ranges = new List<(uint Begin, uint End)>();
        long table = FileOffset(rva);
        for (long at = table; at + 12 <= table + size; at += 12)
        {
            uint begin = BitConverter.ToUInt32(Bytes, (int)at);
            uint end = BitConverter.ToUInt32(Bytes, (int)at + 4);
            uint unwind = BitConverter.ToUInt32(Bytes, (int)at + 8);
            if (begin == 0 || end <= begin) continue;
            ranges.Add((begin, end));
            long info = FileOffset(unwind);
            if ((Bytes[info] >> 3 & 4) == 0) continue;
            int codes = Bytes[info + 2];
            parent[begin] = BitConverter.ToUInt32(Bytes, (int)(info + 4 + 2 * ((codes + 1) & ~1)));
        }

        uint Root(uint begin)
        {
            for (int guard = 0; parent.TryGetValue(begin, out uint up) && guard < 64; guard++) begin = up;
            return begin;
        }

        foreach (var group in ranges.GroupBy(r => Root(r.Begin)))
        {
            ulong end = 0;
            foreach (var (begin, rangeEnd) in group.OrderBy(r => r.Begin))
            {
                // Contiguous pieces only (up to 16 bytes of padding); a piece placed elsewhere is not followed.
                if (begin == group.Key || begin <= end + 16) end = Math.Max(end, rangeEnd);
            }
            FunctionEnds[group.Key] = end;
        }
    }

    private void ReadExports(uint exportRva)
    {
        long dir = FileOffset(exportRva);
        int nameCount = BitConverter.ToInt32(Bytes, (int)dir + 24);
        long functions = FileOffset(BitConverter.ToUInt32(Bytes, (int)dir + 28));
        long names = FileOffset(BitConverter.ToUInt32(Bytes, (int)dir + 32));
        long ordinals = FileOffset(BitConverter.ToUInt32(Bytes, (int)dir + 36));
        for (int i = 0; i < nameCount; i++)
        {
            long nameAt = FileOffset(BitConverter.ToUInt32(Bytes, (int)(names + 4 * i)));
            int end = Array.IndexOf(Bytes, (byte)0, (int)nameAt);
            string name = Encoding.ASCII.GetString(Bytes, (int)nameAt, end - (int)nameAt);
            int ordinal = BitConverter.ToUInt16(Bytes, (int)(ordinals + 2 * i));
            Exports[BitConverter.ToUInt32(Bytes, (int)(functions + 4 * ordinal))] = name;
        }
    }
}
