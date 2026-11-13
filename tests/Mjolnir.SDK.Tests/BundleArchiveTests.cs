using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Mjolnir.SDK.Tests;

/// <summary>
/// The file names inside a bundle's archive, which Unity keys loaded bundles on: read from an archive's header, uncompressed
/// and LZ4, at its start and at its end; what is no archive; and the loaded bundle a refused one clashes with.
/// </summary>
public class BundleArchiveTests
{
    [Fact]
    public void TheNamesAreReadFromAnUncompressedHeader()
    {
        var names = BundleArchive.FileNames(Archive(["CAB-aaa", "CAB-aaa.resS"], lz4: false, atEnd: false));
        Assert.Equal(["CAB-aaa", "CAB-aaa.resS"], names);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheNamesAreReadFromAnLz4Header(bool atEnd)
    {
        var names = BundleArchive.FileNames(Archive(["CAB-209ca4128cc225d140b0877f1e548b48"], lz4: true, atEnd));
        Assert.Equal(["CAB-209ca4128cc225d140b0877f1e548b48"], names);
    }

    [Fact]
    public void Lz4MatchesAreCopiedFromWhatCameBefore()
    {
        // "abc", then 9 bytes from 3 back.
        byte[] block = [0x35, (byte)'a', (byte)'b', (byte)'c', 0x03, 0x00];
        Assert.Equal("abcabcabcabc", Encoding.ASCII.GetString(BundleArchive.Lz4Block(block, 12)));
    }

    [Fact]
    public void WhatIsNoArchiveHasNoNames()
    {
        Assert.Null(BundleArchive.FileNames(Encoding.ASCII.GetBytes("not a bundle at all")));
        Assert.Null(BundleArchive.FileNames(Archive(["CAB-a"], lz4: false, atEnd: false).Take(40).ToArray()));
        Assert.Null(BundleArchive.FileNames(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
    }

    [Fact]
    public void ARefusedBundleNamesTheLoadedOneOfTheSameName()
    {
        string dir = Directory.CreateTempSubdirectory("mjolnir-bundles").FullName;
        try
        {
            string cab = "CAB-" + Guid.NewGuid().ToString("N");
            string first = Path.Combine(dir, "a", "models");
            string second = Path.Combine(dir, "b", "models");
            string other = Path.Combine(dir, "c", "models");
            foreach (string file in new[] { first, second, other }) Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(first, Archive([cab, cab + ".resS"], lz4: true, atEnd: false));
            File.WriteAllBytes(second, Archive([cab, cab + ".resS"], lz4: false, atEnd: false));
            File.WriteAllBytes(other, Archive(["CAB-" + Guid.NewGuid().ToString("N")], lz4: true, atEnd: false));

            Assert.Null(SharedBundles.ClashWith(second)); // nothing loaded yet
            var loaded = new object();
            Assert.Same(loaded, SharedBundles.Get(first, () => loaded));
            Assert.Null(SharedBundles.Get<object>(second, () => null)); // Unity refused it

            Assert.Equal(Path.GetFullPath(first), SharedBundles.ClashWith(second));
            Assert.Contains(Path.GetFullPath(first), SharedBundles.WhyNotLoaded(second));
            Assert.Null(SharedBundles.ClashWith(first)); // itself is no clash
            Assert.Null(SharedBundles.ClashWith(other));
            Assert.Contains("not an AssetBundle", SharedBundles.WhyNotLoaded(other));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ALargeBundleIsReadByItsHeaderAndBlocksInfoAlone(bool atEnd)
    {
        string file = Path.Combine(Path.GetTempPath(), "mjolnir-bundle-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllBytes(file, Archive(["CAB-big", "CAB-big.resS"], lz4: true, atEnd, data: 3 << 20));
            Assert.Equal(["CAB-big", "CAB-big.resS"], BundleArchive.FileNames(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void SizesBeyondTheFileOrBeyondReasonAreNoArchive()
    {
        byte[] archive = Archive(["CAB-a"], lz4: true, atEnd: true);
        int sizes = archive.AsSpan().IndexOf(Encoding.ASCII.GetBytes("6000.3.12f1\0")) + 12 + 8;
        byte[] huge = (byte[])archive.Clone();
        BinaryPrimitives.WriteUInt32BigEndian(huge.AsSpan(sizes + 4), 0xFFFF_FFF0); // the uncompressed size
        Assert.Null(BundleArchive.FileNames(huge));
        byte[] beyond = (byte[])archive.Clone();
        BinaryPrimitives.WriteInt32BigEndian(beyond.AsSpan(sizes), archive.Length + 100); // the compressed size
        Assert.Null(BundleArchive.FileNames(beyond));
    }

    // A UnityFS archive of format 8 with only its header: the blocks info lists one block and these files.
    private static byte[] Archive(IReadOnlyList<string> names, bool lz4, bool atEnd, int data = 64)
    {
        var info = new List<byte>(new byte[16]); // hash
        info.AddRange(BigEndian(1));
        info.AddRange(new byte[10]); // the one block
        info.AddRange(BigEndian(names.Count));
        foreach (string name in names)
        {
            info.AddRange(new byte[20]);
            info.AddRange(Encoding.UTF8.GetBytes(name));
            info.Add(0);
        }
        byte[] raw = info.ToArray();
        byte[] stored = lz4 ? Literals(raw) : raw;

        var head = new List<byte>();
        head.AddRange(Encoding.ASCII.GetBytes("UnityFS\0"));
        head.AddRange(BigEndian(8));
        head.AddRange(Encoding.ASCII.GetBytes("5.x.x\0"));
        head.AddRange(Encoding.ASCII.GetBytes("6000.3.12f1\0"));
        head.AddRange(new byte[8]); // size
        head.AddRange(BigEndian(stored.Length));
        head.AddRange(BigEndian(raw.Length));
        head.AddRange(BigEndian((lz4 ? 3 : 0) | (atEnd ? 0x80 : 0)));
        while (head.Count % 16 != 0) head.Add(0);
        if (atEnd)
        {
            head.AddRange(new byte[data]); // the data
            head.AddRange(stored);
        }
        else
        {
            head.AddRange(stored);
            head.AddRange(new byte[data]);
        }
        return head.ToArray();
    }

    // An LZ4 block of literals only, which is valid LZ4.
    private static byte[] Literals(byte[] data)
    {
        var block = new List<byte>();
        int n = data.Length;
        if (n < 15) block.Add((byte)(n << 4));
        else
        {
            block.Add(0xF0);
            int rest = n - 15;
            for (; rest >= 255; rest -= 255) block.Add(255);
            block.Add((byte)rest);
        }
        block.AddRange(data);
        return block.ToArray();
    }

    private static byte[] BigEndian(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        return bytes;
    }
}
