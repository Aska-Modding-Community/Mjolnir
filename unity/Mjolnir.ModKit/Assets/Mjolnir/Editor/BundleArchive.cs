using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Mjolnir.ModKit
{
    /// <summary>
    /// The names of the files inside a built bundle's archive (<c>CAB-…</c>, <c>CAB-….resS</c>), read from its header (UnityFS,
    /// the blocks info uncompressed or LZ4). Unity keys a loaded bundle on them and refuses a second bundle holding a file of
    /// the same name; they come from the name the bundle was built under (<see cref="BundleBuilder.BuildName"/>), not from its
    /// file name or content. <see cref="ModChecks"/> compares the bundles installed in the game by them. The SDK reads them the
    /// same way to name the bundle a refused one clashes with.
    /// </summary>
    public static class BundleArchive
    {
        /// <summary>The archive's file names; null when the file is no UnityFS archive this reader understands.</summary>
        public static List<string> FileNames(string path)
        {
            try
            {
                return File.Exists(path) ? Read(File.ReadAllBytes(path)) : null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException
                                      || e is IndexOutOfRangeException || e is InvalidDataException || e is OverflowException)
            {
                return null;
            }
        }

        private static List<string> Read(byte[] d)
        {
            int i = 0;
            if (CString(d, ref i) != "UnityFS") return null;
            uint version = U32(d, i);
            i += 4;
            CString(d, ref i);
            CString(d, ref i);
            i += 8;
            int compressed = checked((int)U32(d, i));
            int uncompressed = checked((int)U32(d, i + 4));
            uint flags = U32(d, i + 8);
            i += 12;
            if (version >= 7) i = (i + 15) / 16 * 16;
            int start = (flags & 0x80) != 0 ? d.Length - compressed : i;
            var block = new byte[compressed];
            Array.Copy(d, start, block, 0, compressed);
            byte[] info;
            switch (flags & 0x3F)
            {
                case 0: info = block; break;
                case 2:
                case 3: info = Lz4Block(block, uncompressed); break;
                default: return null;
            }
            int j = 16;
            int blocks = (int)U32(info, j);
            j += 4 + checked(blocks * 10);
            int nodes = (int)U32(info, j);
            j += 4;
            if (nodes < 0 || nodes > 4096) return null;
            var names = new List<string>(nodes);
            for (int n = 0; n < nodes; n++)
            {
                j += 20;
                names.Add(CString(info, ref j));
            }
            return names;
        }

        private static uint U32(byte[] d, int i) => (uint)(d[i] << 24 | d[i + 1] << 16 | d[i + 2] << 8 | d[i + 3]);

        private static string CString(byte[] d, ref int i)
        {
            int end = Array.IndexOf(d, (byte)0, i);
            if (end < 0) throw new InvalidDataException("unterminated string");
            string text = Encoding.UTF8.GetString(d, i, end - i);
            i = end + 1;
            return text;
        }

        private static byte[] Lz4Block(byte[] src, int size)
        {
            var dst = new byte[size];
            int i = 0, o = 0;
            while (i < src.Length && o < size)
            {
                int token = src[i++];
                int literals = token >> 4;
                if (literals == 15)
                    for (byte b = 255; b == 255;) literals += b = src[i++];
                Array.Copy(src, i, dst, o, Math.Min(literals, size - o));
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
}
