using System;
using System.Text;

namespace Mjolnir.SDK;

/// <summary>
/// The 32-bit FNV-1a hash, the same on every machine and in every process (string.GetHashCode changes per process): mods'
/// content ids, which saves and other players keep (the content module), channel ids (the network module), the colour of a
/// mod's letter tile and a counting notification's id (the UI module). Part of the module contract.
/// </summary>
/// <remarks>
/// Two forms, which differ beyond ASCII and must never be swapped for one another, since ids already in saves depend on them:
/// <see cref="OfChars"/> XORs each UTF-16 code unit whole into the hash, <see cref="OfUtf8"/> each byte of the text's UTF-8.
/// </remarks>
internal static class Fnv1a
{
    public const uint OffsetBasis = 2166136261;
    public const uint Prime = 16777619;

    /// <summary>Over the text's UTF-16 code units, each XORed in whole (content ids, notification ids, tile colours).</summary>
    public static uint OfChars(ReadOnlySpan<char> text)
    {
        uint hash = OffsetBasis;
        foreach (char c in text)
        {
            hash ^= c;
            hash *= Prime;
        }
        return hash;
    }

    /// <summary>Over the bytes of the text's UTF-8 (channel ids).</summary>
    public static uint OfUtf8(string text) => OfBytes(Encoding.UTF8.GetBytes(text));

    /// <summary>Over bytes.</summary>
    public static uint OfBytes(ReadOnlySpan<byte> data)
    {
        uint hash = OffsetBasis;
        foreach (byte b in data)
        {
            hash ^= b;
            hash *= Prime;
        }
        return hash;
    }
}
