using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Mjolnir.SDK.Threading;
using Xunit;

namespace Mjolnir.SDK.Tests;

/// <summary>
/// The main library's helpers for the modules: the FNV-1a hash ids depend on, guarded game calls, data from another machine,
/// one wrapper per game object, and a game subscription undone once.
/// </summary>
[Collection("MainThread")]
public sealed class PlatformHelpersTests : IDisposable
{
    public PlatformHelpersTests()
    {
        Dispatcher.ResetForTests();
        GameGuard.ResetForTests();
    }

    public void Dispose()
    {
        Dispatcher.ResetForTests();
        GameGuard.ResetForTests();
    }

    [Fact]
    public void TheHashGivesTheIdsSavesAndOtherPlayersAlreadyHave()
    {
        // Content ids are the char form with the sign bit set: ItemsExample's twine and AltarExample's altar as in saves.
        Assert.Equal(0xCE193218u, Fnv1a.OfChars("mjolnir.test/item/twine") | 0x80000000);
        Assert.Equal(0xE781F742u, Fnv1a.OfChars("mjolnir.itemsexample/item/twine") | 0x80000000);
        Assert.Equal(0xE3E7EA58u, Fnv1a.OfChars("mjolnir.altarexample/building/altar") | 0x80000000);
        Assert.Equal(0xA5F5850Bu, Fnv1a.OfChars("mjolnir.statsexample/effect/blessing"));
        Assert.Equal(189480819u, Fnv1a.OfChars("MyMod/wood")); // a counting notification's id
        Assert.Equal(0xE40C292Cu, Fnv1a.OfUtf8("a"));          // a channel's id
        Assert.Equal(Fnv1a.OffsetBasis, Fnv1a.OfChars(""));
    }

    [Fact]
    public void TheTwoFormsDifferBeyondAscii()
    {
        Assert.Equal(Fnv1a.OfChars("plain"), Fnv1a.OfUtf8("plain"));
        Assert.Equal(0x74AA0AB3u, Fnv1a.OfChars("Moд"));
        Assert.Equal(0x351F46F1u, Fnv1a.OfUtf8("Moд"));
        Assert.Equal(Fnv1a.OfUtf8("Moд"), Fnv1a.OfBytes(Encoding.UTF8.GetBytes("Moд")));
    }

    [Fact]
    public void AGuardedCallNeverThrowsAndAnswersItsFallback()
    {
        bool ran = false;
        GameGuard.Run("SDK.Test", "a call", () => ran = true);
        Assert.True(ran);
        GameGuard.Run("SDK.Test", "a failing call", () => throw new InvalidOperationException("gone"));
        Assert.Equal(5, GameGuard.Try("SDK.Test", "a read", () => 5, -1));
        Assert.Equal(-1, GameGuard.Try<int>("SDK.Test", "a failing read", () => throw new NullReferenceException(), -1));
    }

    [Fact]
    public void AFaultOfACallMadeEveryFrameIsLoggedOnce()
    {
        Assert.False(GameGuard.WasLogged("SDK.Test", "every frame"));
        for (int i = 0; i < 3; i++) GameGuard.RunLogOnce("SDK.Test", "every frame", () => throw new InvalidOperationException());
        Assert.True(GameGuard.WasLogged("SDK.Test", "every frame"));
        Assert.Equal(0, GameGuard.TryLogOnce<int>("SDK.Test", "a read every frame", () => throw new InvalidOperationException(), 0));
        Assert.True(GameGuard.WasLogged("SDK.Test", "a read every frame"));
        Assert.False(GameGuard.WasLogged("SDK.Other", "every frame"));
    }

    [Fact]
    public void RemoteDataIsReadWithinItsBounds()
    {
        byte[] data = Write(w =>
        {
            w.Write((byte)7);
            w.Write("name");
            w.Write(2);
            w.Write(10);
            w.Write(20);
            w.Write(1.5);
        });
        Assert.True(RemoteReader.TryRead(data, 0, r =>
        {
            var values = new List<int>();
            Assert.Equal(7, r.ReadByte());
            Assert.Equal("name", r.ReadString(maxLength: 8));
            int count = r.ReadCount(4);
            for (int i = 0; i < count; i++) values.Add(r.ReadInt32());
            Assert.Equal(1.5, r.ReadDouble());
            Assert.Equal(0, r.Remaining);
            return values;
        }, out var read));
        Assert.Equal([10, 20], read);
    }

    [Fact]
    public void MalformedRemoteDataIsRefused()
    {
        byte[] cut = Write(w => w.Write(5));
        Assert.False(RemoteReader.TryRead(cut, 0, r => r.ReadInt64(), out _));                    // cut short
        Assert.False(RemoteReader.TryRead(Write(w => w.Write(1_000_000)), 0, r => r.ReadCount(4), out _)); // a count beyond the end
        Assert.False(RemoteReader.TryRead(Write(w => w.Write(-1)), 0, r => r.ReadCount(1), out _));
        Assert.False(RemoteReader.TryRead(Write(w => w.Write(3)), 0, r => r.ReadBytes(100), out _));
        Assert.False(RemoteReader.TryRead(Write(w => w.Write("a long name")), 0, r => r.ReadString(maxLength: 4), out _));
        Assert.False(RemoteReader.TryRead([2, 0xC3, 0x28], 0, r => r.ReadString(), out _));         // no UTF-8
        Assert.False(RemoteReader.TryRead([0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F], 0, r => r.ReadString(), out _)); // a length beyond any
        Assert.True(RemoteReader.TryRead([9, 1, 2], 1, r => r.ReadByte(), out byte second));
        Assert.Equal(1, second);
    }

    [Fact]
    public void AGameObjectHasOneWrapperUntilTheGameDestroysIt()
    {
        var released = new List<Wrapper>();
        var wrappers = new GameObjectWrappers<Wrapper>(w => w.Gone, released.Add);
        var first = wrappers.Get(1, () => new Wrapper(1))!;
        Assert.Same(first, wrappers.Get(1, () => new Wrapper(1)));
        Assert.Null(wrappers.Get(2, () => null)); // no such object: nothing kept
        Assert.Equal(1, wrappers.Count);

        first.Gone = true;
        var other = wrappers.Get(3, () => new Wrapper(3))!; // a new one sweeps the gone ones and lets go of what they watched
        Assert.Equal([first], released);
        Assert.Equal(1, wrappers.Count);
        Assert.NotSame(first, wrappers.Get(1, () => new Wrapper(1)));
        Assert.False(other.Gone);
    }

    [Fact]
    public void OnAnotherThreadNothingIsSwept()
    {
        Dispatcher.MarkMainThread(); // this thread is the game's main one, the task's another
        var released = new List<Wrapper>();
        var wrappers = new GameObjectWrappers<Wrapper>(w => w.Gone, released.Add);
        wrappers.Get(1, () => new Wrapper(1))!.Gone = true;
        var other = new Thread(() => wrappers.Get(2, () => new Wrapper(2)));
        other.Start();
        other.Join();
        Assert.Empty(released);
        Assert.Equal(2, wrappers.Count);
        wrappers.Get(3, () => new Wrapper(3));
        Assert.Single(released);
    }

    [Fact]
    public void AGameSubscriptionIsUndoneOnceAndItsFaultIsLogged()
    {
        int undone = 0;
        var subscription = new GameSubscription("SDK.Test", () => undone++);
        subscription.Dispose();
        subscription.Dispose();
        Assert.Equal(1, undone);
        new GameSubscription("SDK.Test", () => throw new InvalidOperationException("the game object is gone")).Dispose();
        GameSubscription.None.Dispose();
    }

    private static byte[] Write(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true)) write(writer);
        return stream.ToArray();
    }

    private sealed class Wrapper(int id)
    {
        public bool Gone { get; set; }
        public override string ToString() => $"wrapper {id}";
    }
}
