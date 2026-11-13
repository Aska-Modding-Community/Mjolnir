using System;
using System.Collections.Generic;
using Mjolnir.SDK;
using Mjolnir.SDK.AI;
using Mjolnir.SDK.Config;
using Mjolnir.SDK.Player;
using Mjolnir.SDK.UI;
using UnityEngine;

namespace Mjolnir.AnimationsExample;

/// <summary>
/// Example mod: the game's own animations, played on the player and on villagers by name (<c>Animations.Find</c>; the Mjolnir
/// kit lists them all, with every character to see them on).
/// <list type="bullet">
/// <item>the player raises a toast with a key (<c>Keys.Toast</c>, F9: <c>aska_idle_skal</c>) and stands while it plays;
/// villagers within 12 m toast back where they stand (<c>peasant_male_idle_skal</c>, which the village women play too),
/// through a job of their own whose step plays it (<see cref="ResidentJob.Play(string, AnimationOptions?, TimeSpan?)"/>).
/// A job, not <c>resident.Animator.Play</c> alone: a villager goes on with its work under an animation played on it, walking
/// and carrying; the job takes it from its work for the toast, as the game's own tasks do, and gives it back after. Its
/// priority says who answers: only for a few seconds after the toast, within 12 m, once each, and never a sleeper;</item>
/// <item>a dodge forward (<c>Keys.Dodge</c>, F10: <c>aska_dodge_front</c>) moves the player itself: it plays with root
/// motion, so the game's movement takes it, walls and ground included;</item>
/// <item>a wave (<c>Keys.Wave</c>, F11: <c>aska_wave</c>) through the upper-body mask, so the player can wave while walking;</item>
/// <item>the console's <c>toast</c> and <c>dodge</c>.</item>
/// </list>
/// The game's clips are known once the game has loaded them (its main menu), so they are found when played, not in
/// <see cref="OnLoad"/>. In multiplayer the other players see them where they run this mod too; the dodge's movement is the game's.
/// </summary>
[ModManifest(
    "mjolnir.animationsexample",
    "Animations Example",
    "1.1.0",
    Description = "The game's own animations: a toast villagers answer, a dodge forward, a wave while walking.",
    Author = "Mjolnir")]
public sealed class AnimationsExampleMod : Mod
{
    private const string Toast = "aska_idle_skal";
    private const string ToastBack = "peasant_male_idle_skal";
    private const string Dodge = "aska_dodge_front";
    private const string Wave = "aska_wave";
    private const float ToastBackWithin = 12f; // metres
    private static readonly TimeSpan ToastBackFor = TimeSpan.FromSeconds(4);

    private readonly HashSet<Resident> _toastedBack = [];
    private ResidentJob? _toastBack;
    private DateTime _toastedAt = DateTime.MinValue;
    private Vector3 _toastedWhere;

    public override void OnLoad()
    {
        var toastKey = Config.Bind("Keys", "Toast", new KeyGesture("F9"), "The player raises a toast; villagers near toast back.").WithDisplayName("Toast");
        var dodgeKey = Config.Bind("Keys", "Dodge", new KeyGesture("F10"), "A dodge forward: the animation moves the player.").WithDisplayName("Dodge");
        var waveKey = Config.Bind("Keys", "Wave", new KeyGesture("F11"), "A wave, walking on.").WithDisplayName("Wave");

        // Villagers near a toast answer it: a job that only asks for a few seconds after the player's, once per villager.
        // The villager is counted once its toast is over: a job whose priority drops while it runs is given up, so it
        // keeps asking for as long as it plays.
        _toastBack = Residents.AddJob("toast_back", ToastBackPriority)
                              .Play(ToastBack)
                              .Do(resident => _toastedBack.Add(resident));

        HotKeys.Add(toastKey, RaiseToast);
        HotKeys.Add(dodgeKey, DodgeForward);
        HotKeys.Add(waveKey, WaveWalking);
        DevConsole.Add("toast", "The player raises a toast; villagers near toast back.", _ => RaiseToast());
        DevConsole.Add("dodge", "The player dodges forward.", _ => DodgeForward());
    }

    private void RaiseToast()
    {
        if (!LocalPlayer.IsSpawned || Animations.Find(Toast) is not { } toast) return;
        LocalPlayer.Animator.Play(toast); // the whole body: the player stands while it plays
        _toastedAt = DateTime.UtcNow;
        _toastedWhere = LocalPlayer.Position;
        _toastedBack.Clear();
    }

    private void DodgeForward()
    {
        if (!LocalPlayer.IsSpawned || Animations.Find(Dodge) is not { } dodge) return;
        LocalPlayer.Animator.Play(dodge, new AnimationOptions
        {
            RootMotion = true,
            FadeIn = TimeSpan.FromSeconds(0.05),
            FadeOut = TimeSpan.FromSeconds(0.15),
        });
    }

    private void WaveWalking()
    {
        if (!LocalPlayer.IsSpawned || Animations.Find(Wave) is not { } wave) return;
        LocalPlayer.Animator.Play(wave, new AnimationOptions { Mask = AnimationMask.UpperBody }); // the legs walk on
    }

    // 25 is above work (15) and above sleep at its hour (21): villagers near answer whatever they were doing.
    private float ToastBackPriority(Resident resident)
    {
        if (_toastBack != null && resident.ModJob == _toastBack) return 25f; // toasting now: let it finish
        if (DateTime.UtcNow - _toastedAt > ToastBackFor || _toastedBack.Contains(resident)) return -1f;
        return Vector3.Distance(resident.Position, _toastedWhere) <= ToastBackWithin ? 25f : -1f;
    }
}
