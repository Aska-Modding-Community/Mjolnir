using System;
using System.IO;
using System.Text;

namespace Mjolnir.SDK;

/// <summary>
/// Reads data that came from another machine (a packet, a message of the platform's own), which may be cut, damaged or made
/// to harm: every read stays inside the data, a count is refused when the rest cannot hold that many elements, and
/// <see cref="TryRead{T}"/> turns whatever such data makes the reader throw into a refusal. Little-endian, strings as
/// <see cref="BinaryWriter"/> writes them (UTF-8 with a 7-bit encoded length). Part of the module contract.
/// </summary>
internal sealed class RemoteReader
{
    private readonly BinaryReader _reader;
    private readonly MemoryStream _stream;

    /// <summary>A reader of <paramref name="count"/> bytes of the data from <paramref name="offset"/> (the rest without a count).</summary>
    public RemoteReader(byte[] data, int offset = 0, int? count = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        _stream = new MemoryStream(data, offset, count ?? data.Length - offset, writable: false);
        _reader = new BinaryReader(_stream, new UTF8Encoding(false, throwOnInvalidBytes: true));
    }

    /// <summary>The bytes left to read.</summary>
    public long Remaining => _stream.Length - _stream.Position;

    public byte ReadByte() => _reader.ReadByte();
    public bool ReadBoolean() => _reader.ReadBoolean();
    public int ReadInt32() => _reader.ReadInt32();
    public uint ReadUInt32() => _reader.ReadUInt32();
    public long ReadInt64() => _reader.ReadInt64();
    public float ReadSingle() => _reader.ReadSingle();
    public double ReadDouble() => _reader.ReadDouble();

    /// <summary>A string of at most <paramref name="maxLength"/> characters; a longer one or bytes that are no UTF-8 are refused.</summary>
    public string ReadString(int maxLength = int.MaxValue)
    {
        string text = _reader.ReadString();
        if (text.Length > maxLength) throw new InvalidDataException($"a string of {text.Length} characters, at most {maxLength} expected");
        return text;
    }

    /// <summary>Exactly <paramref name="count"/> bytes; fewer left, or a negative count, is refused.</summary>
    public byte[] ReadBytes(int count)
    {
        if (count < 0 || count > Remaining) throw new EndOfStreamException($"{count} bytes asked for, {Remaining} left");
        return _reader.ReadBytes(count);
    }

    /// <summary>
    /// A count (an Int32) of elements of at least <paramref name="elementSize"/> bytes each that the rest can hold; a negative
    /// count or one beyond the data's end is refused, so no array is made for elements that are not there.
    /// </summary>
    public int ReadCount(int elementSize)
    {
        int count = _reader.ReadInt32();
        if (count < 0 || (long)count * Math.Max(1, elementSize) > Remaining) throw new EndOfStreamException("a count beyond the data's end");
        return count;
    }

    /// <summary>
    /// The result of <paramref name="read"/> over the data, or false when the data is malformed (<see cref="IsMalformed"/>):
    /// cut short, a count or a length beyond its end, a string that is no UTF-8, a number out of range.
    /// </summary>
    public static bool TryRead<T>(byte[] data, int offset, Func<RemoteReader, T> read, out T result)
    {
        try
        {
            result = read(new RemoteReader(data, offset));
            return true;
        }
        catch (Exception e) when (IsMalformed(e))
        {
            result = default!;
            return false;
        }
    }

    /// <summary>Whether an exception is one malformed data makes a reader throw (never a fault of the reading code's own kind).</summary>
    public static bool IsMalformed(Exception e) =>
        e is EndOfStreamException or IOException or InvalidDataException or ArgumentException or FormatException
            or DecoderFallbackException or OverflowException;
}
