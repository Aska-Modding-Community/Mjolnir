using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Mjolnir.SDK;

/// <summary>
/// The names of the files inside an AssetBundle's archive (<c>CAB-…</c>, <c>CAB-….resS</c>). Unity keys a loaded bundle on
/// them and refuses a second bundle that holds a file of the same name ("another AssetBundle with the same files is already
/// loaded"); they come from the name the bundle was built under, not from its file name or content (two different bundles
/// both built as <c>models</c> hold <c>CAB-209ca412…</c>). Read from the archive's header (UnityFS, the blocks info
/// uncompressed or LZ4), so the SDK can name the bundle a refused one clashes with. Only the header and the blocks info are
/// read, never the whole bundle. Pure: no Unity calls.
/// </summary>
internal static class BundleArchive
{
    // The header's strings and sizes fit in far less; the blocks info of a bundle of thousands of files in a few hundred KB.
    private const int HeadSize = 4096;
    private const int MaxInfoSize = 16 << 20;

    /// <summary>The archive's file names; null when the file is no UnityFS archive this reader understands.</summary>
    public static IReadOnlyList<string>? FileNames(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var head = new byte[(int)Math.Min(HeadSize, file.Length)];
            file.ReadExactly(head);
            return Parse(head, file.Length, (offset, count) =>
            {
                var bytes = new byte[count];
                file.Position = offset;
                file.ReadExactly(bytes);
                return bytes;
            });
        }
        catch (Exception e) when (IsUnreadable(e) || e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The same, from the archive's bytes.</summary>
    public static IReadOnlyList<string>? FileNames(byte[] data)
    {
        try
        {
            return Parse(data, data.Length, (offset, count) => data.AsSpan(checked((int)offset), count).ToArray());
        }
        catch (Exception e) when (IsUnreadable(e))
        {
            return null;
        }
    }

    // What a file that is no archive, or a damaged one, makes the reader throw.
    private static bool IsUnreadable(Exception e) =>
        e is ArgumentException or IndexOutOfRangeException or InvalidDataException or OverflowException or EndOfStreamException;

    // head: the file's first bytes (all of them for a small file); read: count bytes at an offset of the file.
    private static List<string>? Parse(byte[] head, long length, Func<long, int, byte[]> read)
    {
        int i = 0;
        if (CString(head, ref i) != "UnityFS") return null;
        uint version = BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(i));
        i += 4;
        CString(head, ref i); // the Unity version the format names ("5.x.x")
        CString(head, ref i); // the editor's revision
        i += 8; // the archive's size
        int compressed = checked((int)BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(i)));
        int uncompressed = checked((int)BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(i + 4)));
        uint flags = BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(i + 8));
        i += 12;
        if (version >= 7) i = (i + 15) / 16 * 16;
        if (compressed > MaxInfoSize || uncompressed > MaxInfoSize) throw new InvalidDataException("blocks info size");
        long at = (flags & 0x80) != 0 ? length - compressed : i;
        if (at < 0 || at + compressed > length) throw new InvalidDataException("blocks info outside the file");
        byte[] block = at + compressed <= head.Length ? head.AsSpan((int)at, compressed).ToArray() : read(at, compressed);
        byte[] info = (flags & 0x3F) switch
        {
            0 => block,
            2 or 3 => Lz4Block(block, uncompressed),
            _ => throw new InvalidDataException("blocks info compressed other than with LZ4"),
        };
        int j = 16; // the data's hash
        int blocks = BinaryPrimitives.ReadInt32BigEndian(info.AsSpan(j));
        if (blocks < 0) throw new InvalidDataException("block count");
        j += 4 + checked(blocks * 10);
        int nodes = BinaryPrimitives.ReadInt32BigEndian(info.AsSpan(j));
        j += 4;
        if (nodes < 0 || nodes > 4096) throw new InvalidDataException("node count");
        var names = new List<string>(nodes);
        for (int n = 0; n < nodes; n++)
        {
            j += 20; // offset, size, flags
            names.Add(CString(info, ref j));
        }
        return names;
    }

    private static string CString(byte[] d, ref int i)
    {
        int end = Array.IndexOf(d, (byte)0, i);
        if (end < 0) throw new InvalidDataException("unterminated string");
        string text = Encoding.UTF8.GetString(d, i, end - i);
        i = end + 1;
        return text;
    }

    /// <summary>An LZ4 block (no frame) decoded into <paramref name="size"/> bytes.</summary>
    internal static byte[] Lz4Block(ReadOnlySpan<byte> src, int size)
    {
        var dst = new byte[size];
        int i = 0, o = 0;
        while (i < src.Length && o < size)
        {
            int token = src[i++];
            int literals = token >> 4;
            if (literals == 15)
                for (byte b = 255; b == 255;) literals += b = src[i++];
            src.Slice(i, Math.Min(literals, size - o)).CopyTo(dst.AsSpan(o));
            i += literals;
            o += literals;
            if (i >= src.Length || o >= size) break;
            int offset = src[i] | (src[i + 1] << 8);
            i += 2;
            if (offset == 0 || offset > o) throw new InvalidDataException("LZ4 offset");
            int length = token & 15;
            if (length == 15)
                for (byte b = 255; b == 255;) length += b = src[i++];
            length += 4;
            for (int k = 0; k < length && o < size; k++, o++) dst[o] = dst[o - offset];
        }
        return dst;
    }
}
