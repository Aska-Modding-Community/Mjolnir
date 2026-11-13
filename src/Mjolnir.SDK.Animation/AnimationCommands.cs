using System;
using System.Globalization;
using System.Linq;
using SSSGame;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.Animation;

/// <summary>
/// The animation module's console commands (<see cref="DevConsole"/>): <c>anims</c> lists animations, <c>anim</c> plays one on
/// the player or the nearest villager, <c>animstop</c> stops it, <c>emote</c> plays one of the game's emotes.
/// </summary>
internal static class AnimationCommands
{
    private const int ShownAtMost = 40;

    public static void Register()
    {
        // The platform's own commands, whichever mod's code happens to be running.
        var platform = typeof(AnimationCommands).Assembly;
        DevConsole.AddFor(platform, "anims", "Lists the animations whose names contain the words.", List, "[words]");
        DevConsole.AddFor(platform, "anim", "Plays an animation on the player, or with 'villager' on the nearest villager.", Play,
                                    "<animation> [loop] [upper] [move] [villager]");
        DevConsole.AddFor(platform, "animstop", "Stops the animation of the player, or with 'villager' of the nearest villager.", Stop, "[villager]");
        DevConsole.AddFor(platform, "emote", "Plays one of the game's emotes on the player, or with 'villager' on the nearest villager.", PlayEmote,
                                    "<sit|wave|cheer|skal> [villager]");
    }

    private static void List(DevCommandContext context)
    {
        string words = context.Arguments.Count > 0 ? context.GetRest(0) : "";
        var found = Animations.All.Where(a => a.FullName.Contains(words, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var animation in found.Take(ShownAtMost))
            context.Print(Number($"{animation.FullName}  {animation.Length.TotalSeconds:0.00} s"));
        if (found.Count > ShownAtMost) context.Print($"  and {found.Count - ShownAtMost} more");
        if (found.Count == 0) context.Print($"No animation contains '{words}'.");
    }

    private static void Play(DevCommandContext context)
    {
        string name = context.GetString(0);
        var animation = Animations.Find(name) ?? throw new DevCommandException($"No animation called '{name}'. anims <words> lists them.");
        var flags = context.Arguments.Skip(1).Select(a => a.ToLowerInvariant()).ToHashSet();
        var (animator, who) = Target(context, flags.Contains("villager"));
        var playback = animator.Play(animation, new AnimationOptions
        {
            Loop = flags.Contains("loop"),
            Mask = flags.Contains("upper") ? AnimationMask.UpperBody : null,
            RootMotion = flags.Contains("move"),
        });
        playback.Ended += (_, e) => DevConsole.Print(Number($"{animation.FullName} on {who} {(e.WasStopped ? "stopped" : "ended")} at {playback.Time.TotalSeconds:0.00} s."));
        context.Print(Number($"{animation.FullName} ({animation.Length.TotalSeconds:0.00} s) on {who}."));
    }

    private static void Stop(DevCommandContext context)
    {
        var (animator, who) = Target(context, context.Arguments.Any(a => a.Equals("villager", StringComparison.OrdinalIgnoreCase)));
        if (animator.Current == null) context.Print($"Nothing plays on {who}.");
        else animator.Stop();
    }

    private static void PlayEmote(DevCommandContext context)
    {
        string name = context.GetString(0);
        if (!Enum.TryParse<Emote>(name, ignoreCase: true, out var emote) || !Enum.IsDefined(emote))
            throw new DevCommandException($"No emote called '{name}': sit, wave, cheer or skal.");
        var (animator, who) = Target(context, context.Arguments.Skip(1).Any(a => a.Equals("villager", StringComparison.OrdinalIgnoreCase)));
        if (animator.PlayEmote(emote)) context.Print($"{emote} on {who}.");
        else context.Error($"{who} has no {emote} emote.");
    }

    // The player, or the villager nearest to it.
    private static (CharacterAnimator Animator, string Who) Target(DevCommandContext context, bool villager)
    {
        var player = GameAnimations.LocalPlayer();
        if (player == null) throw new DevCommandException("No player in the world.");
        if (!villager) return (CharacterAnimator.Of(player.Pointer), "the player");
        var here = player.transform.position;
        var nearest = Object.FindObjectsOfType<Villager>().OrderBy(v => Vector3.Distance(v.transform.position, here)).FirstOrDefault();
        if (nearest == null) throw new DevCommandException("No villager in the world.");
        return (CharacterAnimator.Of(nearest.Pointer), Number($"{nearest.GetName()} ({Vector3.Distance(nearest.transform.position, here):0} m)"));
    }

    private static string Number(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
