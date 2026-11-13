using System;
using System.IO;
using System.Text;

namespace Mjolnir.SDK.Animation;

/// <summary>
/// The animation messages between players (pure: unit-tested), on the platform's channel <c>animation</c>: a character,
/// by its network object's id, started an animation (its full name and options) or stopped it. Mods' animations are found
/// on the receiving side by their full names, so a player that does not run the mod shows nothing.
/// </summary>
internal static class AnimationWire
{
    public const string Channel = "animation";
    private const byte Version = 1;
    private const byte PlayKind = 1, StopKind = 2;
    private const int MaxName = 1024; // an animation's or a mask's full name: far beyond any real one

    public static byte[] Play(uint character, string animation, AnimationOptions options)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Version);
            writer.Write(PlayKind);
            writer.Write(character);
            writer.Write(animation);
            writer.Write(options.Loop);
            writer.Write(options.Speed);
            writer.Write(options.FadeIn.Ticks);
            writer.Write(options.FadeOut.Ticks);
            writer.Write(options.Duration?.Ticks ?? -1);
            writer.Write(options.Mask?.ToString() ?? "");
        }
        return stream.ToArray();
    }

    public static byte[] Stop(uint character) => [Version, StopKind, .. BitConverter.GetBytes(character)];

    /// <summary>
    /// A message read: a play with its animation's full name and options (the mask found by <paramref name="mask"/>), or a
    /// stop (<c>Animation</c> null). False for anything else: another version, cut short or broken (the message comes from
    /// another machine, so nothing in it throws), a mask this side does not have.
    /// </summary>
    public static bool TryRead(byte[] data, Func<string, AnimationMask?> mask, out uint character, out string? animation, out AnimationOptions? options)
    {
        character = 0;
        animation = null;
        options = null;
        if (!RemoteReader.TryRead(data, 0, reader => Read(reader, mask), out var message) || message is not { } read) return false;
        (character, animation, options) = read;
        return true;
    }

    private static (uint Character, string? Animation, AnimationOptions? Options)? Read(RemoteReader reader, Func<string, AnimationMask?> mask)
    {
        if (reader.ReadByte() != Version) return null;
        byte kind = reader.ReadByte();
        uint character = reader.ReadUInt32();
        if (kind == StopKind) return (character, null, null);
        if (kind != PlayKind) return null;
        string animation = reader.ReadString(MaxName);
        bool loop = reader.ReadBoolean();
        double speed = reader.ReadDouble();
        long fadeIn = reader.ReadInt64(), fadeOut = reader.ReadInt64(), duration = reader.ReadInt64();
        string maskName = reader.ReadString(MaxName);
        AnimationMask? found = null;
        if (maskName.Length > 0 && (found = mask(maskName)) == null) return null;
        return (character, animation, new AnimationOptions
        {
            Loop = loop,
            Speed = speed,
            FadeIn = TimeSpan.FromTicks(Math.Max(0, fadeIn)),
            FadeOut = TimeSpan.FromTicks(Math.Max(0, fadeOut)),
            Duration = duration < 0 ? null : TimeSpan.FromTicks(duration),
            Mask = found,
        });
    }
}
