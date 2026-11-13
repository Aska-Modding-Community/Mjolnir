using System;
using System.Collections.Generic;
using System.Reflection;
using Xunit;

namespace Mjolnir.SDK.AI.Tests;

/// <summary>An interaction's object in the game as the model sees it, set by the test.</summary>
internal sealed class FakeTarget : IInteractionTarget
{
    public bool Exists { get; set; } = true;
    public UnityEngine.GameObject? Object => null;
    public (float X, float Y, float Z) Position { get; set; }
    public bool PlayerCanUse { get; set; } = true;
    public int Removed { get; private set; }

    public void Remove() => Removed++;
}

/// <summary>
/// Interactions of a mod's own on fake objects: names and owners, the nearest free one, settings and their versions, turns
/// and how many at once, a removed or gone one, and the player's use (asked, then reported; a cancel).
/// </summary>
public class InteractionsTests
{
    private static readonly Assembly Caller = typeof(InteractionsTests).Assembly;

    internal static (ModInteraction Interaction, FakeTarget Target) Make(InteractionRegistry? registry = null, string name = "altar", (float, float, float) at = default)
    {
        var target = new FakeTarget { Position = at };
        var interaction = (registry ?? new InteractionRegistry(_ => "mjolnir.test")).Add(Caller, name, _ => target);
        return (interaction, target);
    }

    [Fact]
    public void AnInteractionIsTheCallingModsAndNamedOnly()
    {
        var registry = new InteractionRegistry(_ => "mjolnir.test");
        var (altar, _) = Make(registry);
        var (second, _) = Make(registry); // several of a kind share the name
        Assert.Equal(("mjolnir.test", "altar", "mjolnir.test.altar"), (altar.ModId, altar.Name, altar.ToString()));
        Assert.Equal([altar, second], registry.All);
        Assert.Throws<ArgumentException>(() => Make(registry, "an altar"));
        Assert.Throws<InvalidOperationException>(() => Make(new InteractionRegistry(_ => null)));
        Assert.Equal("altar", altar.ShownText);
        altar.WithText("Pray");
        Assert.Equal("Pray", altar.ShownText);
    }

    [Fact]
    public void SettingsAreCheckedAndChangeTheVersion()
    {
        var (altar, _) = Make();
        int version = altar.Version;
        Assert.Same(altar, altar.WithHoldTime(TimeSpan.FromSeconds(2)).WithDistance(2.5f).WithMaxUsers(3).WithText("Pray"));
        Assert.Equal((TimeSpan.FromSeconds(2), 2.5f, 3, "Pray"), (altar.HoldTime, altar.Distance, altar.MaxUsers, altar.Text));
        Assert.Equal(version + 4, altar.Version);
        altar.MaxUsers = 3; // the same value: nothing to show again
        Assert.Equal(version + 4, altar.Version);
        Assert.Throws<ArgumentOutOfRangeException>(() => altar.HoldTime = TimeSpan.FromSeconds(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => altar.Distance = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => altar.Distance = float.NaN);
        Assert.Throws<ArgumentOutOfRangeException>(() => altar.MaxUsers = 0);
    }

    [Fact]
    public void TurnsAreTakenUpToMaxUsers()
    {
        var (altar, _) = Make();
        object a = new(), b = new();
        Assert.True(altar.IsFree);
        Assert.True(altar.TryReserve(a));
        Assert.False(altar.TryReserve(b)); // one at a time by default
        Assert.False(altar.IsFree);
        altar.MaxUsers = 2;
        Assert.False(altar.TryReserve(a)); // the same user twice
        Assert.True(altar.TryReserve(b));
        Assert.Equal(2, altar.UserCount);
        altar.Release(a);
        altar.Release(a);
        Assert.Equal(1, altar.UserCount);
        altar.IsEnabled = false;
        Assert.False(altar.IsFree);
        Assert.False(altar.TryReserve(a));
    }

    [Fact]
    public void AGoneOrRemovedInteractionIsNotAvailable()
    {
        var registry = new InteractionRegistry(_ => "mjolnir.test");
        var (altar, target) = Make(registry);
        target.Exists = false;
        Assert.False(altar.IsAvailable);
        Assert.Empty(registry.All);
        var (other, otherTarget) = Make(registry);
        Assert.True(other.TryReserve(new object()));
        Assert.True(registry.Remove(other));
        Assert.False(registry.Remove(other));
        Assert.Equal(1, otherTarget.Removed);
        Assert.False(other.IsAvailable);
        Assert.Equal(0, other.UserCount);
        Assert.False(other.TryReserve(new object()));
    }

    [Fact]
    public void TheNearestFreeOneOfTheModsKind()
    {
        var registry = new InteractionRegistry(_ => "mjolnir.test");
        var (far, _) = Make(registry, at: (20, 0, 0));
        var (near, _) = Make(registry, at: (5, 30, 0)); // across the ground: the height does not count
        Make(registry, "well", at: (1, 0, 0));
        Assert.Same(near, registry.Nearest(Caller, (0, 0, 0), "altar", float.PositiveInfinity));
        Assert.True(near.TryReserve(new object()));
        Assert.Same(far, registry.Nearest(Caller, (0, 0, 0), "altar", float.PositiveInfinity));
        Assert.Null(registry.Nearest(Caller, (0, 0, 0), "altar", 10));
        Assert.Null(new InteractionRegistry(_ => "mjolnir.other").Nearest(Caller, (0, 0, 0), "altar", float.PositiveInfinity));
    }

    [Fact]
    public void EveryBuildingOfAKindGetsItsOwnOnceAndTheModSettlesIt()
    {
        var registry = new InteractionRegistry(_ => "mjolnir.test");
        Assert.False(registry.HasBuildings);
        var settled = new List<ModInteraction>();
        registry.AddToBuildings(Caller, "Item_mjolnir.test_altar", "altar", a => settled.Add(a.WithText("Pray")));
        Assert.True(registry.HasBuildings);
        var targets = new Dictionary<nint, FakeTarget>();
        IInteractionTarget At(nint building) => targets[building] = new FakeTarget();
        registry.BuildingAppeared(1, "Item_mjolnir.test_altar", _ => At(1));
        registry.BuildingAppeared(1, "Item_mjolnir.test_altar", _ => At(1)); // the same building set up again
        registry.BuildingAppeared(2, "Item_mjolnir.test_altar", _ => At(2));
        registry.BuildingAppeared(3, "Item_Structures_WaterWellL1", _ => At(3)); // another kind
        Assert.Equal(2, settled.Count);
        Assert.All(settled, a => Assert.Equal(("altar", "Pray"), (a.Name, a.Text)));
        Assert.Equal(settled, registry.All);
        targets[1].Exists = false; // dismantled; a new building may take its identity
        registry.BuildingAppeared(1, "Item_mjolnir.test_altar", _ => At(1));
        Assert.Equal(3, settled.Count);
        Assert.Equal(2, registry.BuildingsKnown);
        targets[2].Exists = false; // dismantled for good: let go of
        Assert.Single(registry.All);
        Assert.Equal(1, registry.BuildingsKnown);
        Assert.True(registry.Remove(registry.All[0]));
        Assert.Equal(0, registry.BuildingsKnown);
        Assert.Throws<ArgumentException>(() => registry.AddToBuildings(Caller, " ", "altar", _ => { }));
        Assert.Throws<ArgumentException>(() => registry.AddToBuildings(Caller, "Item_X", "an altar", _ => { }));
        Assert.Throws<InvalidOperationException>(() => new InteractionRegistry(_ => null).AddToBuildings(Caller, "Item_X", "altar", _ => { }));
    }

    [Fact]
    public void OnAnObjectThatIsNoBuildingOnlyVillagersUseIt()
    {
        var target = new FakeTarget { PlayerCanUse = false };
        var stone = new InteractionRegistry(_ => "mjolnir.test").Add(Caller, "stone", _ => target);
        Assert.False(stone.PlayerCanUse);
        int used = 0;
        stone.Used += (_, _) => used++;
        Assert.False(stone.PlayerUsed());
        Assert.True(stone.TryReserve(new object()));
        Assert.Equal(0, used);
    }

    [Fact]
    public void ThePlayersUseIsAskedThenReported()
    {
        var (altar, _) = Make();
        var seen = new List<string>();
        altar.Using += (sender, e) => seen.Add($"using {e.IsPlayer} {sender == altar}");
        altar.Used += (_, e) => seen.Add($"used {e.IsPlayer} {e.Resident == null}");
        Assert.True(altar.PlayerUsed());
        Assert.Equal(["using True True", "used True True"], seen);
        seen.Clear();
        EventHandler<InteractionUsingEventArgs> cancel = (_, e) => e.Cancel = true;
        altar.Using += cancel;
        Assert.False(altar.PlayerUsed());
        Assert.Equal(["using True True"], seen);
        altar.Using -= cancel;
        altar.PlayerCanUse = false;
        seen.Clear();
        Assert.False(altar.PlayerUsed());
        Assert.Empty(seen);
        altar.PlayerCanUse = true;
        Assert.True(altar.TryReserve(new object())); // full: a villager is at it
        Assert.False(altar.PlayerUsed());
    }
}

/// <summary>A villager using an interaction in a job: the walk to it, its turn, the time at it, the events, and letting go.</summary>
public class UseStepTests
{
    private static readonly Assembly Caller = typeof(UseStepTests).Assembly;

    private static (JobRun Run, FakeVillager Villager, ModInteraction Altar) Run(TimeSpan duration, Action<ModInteraction>? setUp = null)
    {
        var (altar, _) = InteractionsTests.Make(at: (10, 0, 0));
        setUp?.Invoke(altar);
        var job = new JobRegistry(_ => "mjolnir.test").Add(Caller, "pray", _ => 1);
        job.Use(_ => altar, duration);
        var villager = new FakeVillager(9301, "Sigrid");
        return (new JobRun(job, new Resident(villager)), villager, altar);
    }

    // As the game side does when the walk starts: the interaction the mod gives, and its place.
    private static void Approach(JobRun run, ModInteraction altar, double now)
    {
        run.Begin(0, now);
        run.Interaction = altar;
        run.Target = altar.PositionXyz;
    }

    [Fact]
    public void UseIsAWalkToItThenATurnAtIt()
    {
        var (run, _, _) = Run(TimeSpan.FromSeconds(3));
        Assert.Collection(run.Steps, s => Assert.IsType<ApproachStep>(s), s => Assert.Equal(TimeSpan.FromSeconds(3), Assert.IsType<UseStep>(s).Duration));
        var job = new JobRegistry(_ => "mjolnir.test").Add(Caller, "bad", _ => 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => job.Use(_ => null, TimeSpan.FromSeconds(-1)));
        Assert.Throws<ArgumentNullException>(() => job.Use(null!, TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => job.Use(_ => null, TimeSpan.Zero, " "));
        Assert.Empty(job.Steps);
    }

    [Fact]
    public void TheWalkEndsWithinTheInteractionsDistance()
    {
        var (run, villager, altar) = Run(TimeSpan.FromSeconds(1), a => a.Distance = 2);
        run.Begin(0, now: 0);
        Assert.True(run.IsOver(0)); // the mod gave none
        Approach(run, altar, now: 0);
        villager.Position = (7, 0, 0);
        Assert.False(run.IsOver(1));
        villager.Position = (8.5f, 0, 0);
        Assert.True(run.IsOver(1));
        Approach(run, altar, now: 2);
        altar.IsEnabled = false; // passed by while disabled
        Assert.True(run.IsOver(2));
    }

    [Fact]
    public void ATurnTakenIsUsedAfterItsTime()
    {
        var (run, villager, altar) = Run(TimeSpan.FromSeconds(3));
        var seen = new List<string>();
        altar.Using += (_, e) => seen.Add($"using {e.Resident?.Name}");
        altar.Used += (_, e) => seen.Add($"used {e.Resident?.Name}");
        Approach(run, altar, now: 0);
        run.Begin(1, now: 5);
        Assert.False(run.IsOver(5));
        Assert.True(run.IsUsing);
        Assert.Equal(1, altar.UserCount);
        Assert.Equal((10f, 0f, 0f), villager.Faced);
        Assert.False(run.IsOver(7.9));
        Assert.True(run.IsOver(8));
        Assert.Equal(["using Sigrid", "used Sigrid"], seen);
        Assert.Equal(0, altar.UserCount);
    }

    [Fact]
    public void AVillagerWaitsForItsTurnAMinuteAtMost()
    {
        var (run, _, altar) = Run(TimeSpan.FromSeconds(1));
        object other = new();
        Assert.True(altar.TryReserve(other));
        Approach(run, altar, now: 0);
        run.Begin(1, now: 0);
        Assert.False(run.IsOver(30));
        Assert.False(run.IsUsing);
        altar.Release(other);
        Assert.False(run.IsOver(31)); // its turn now
        Assert.True(run.IsUsing);
        Assert.True(run.IsOver(32));

        Assert.True(altar.TryReserve(other));
        run.Begin(1, now: 100);
        Assert.True(run.IsOver(100 + JobRun.MoveTimeout.TotalSeconds));
    }

    [Fact]
    public void AVillagersOwnTurnKeepsItsJobAsking()
    {
        var registry = new InteractionRegistry(_ => "mjolnir.test");
        var (altar, _) = InteractionsTests.Make(registry, at: (10, 0, 0));
        var job = new JobRegistry(_ => "mjolnir.test")
            .Add(Caller, "pray", r => registry.Nearest(Caller, r.PositionXyz, "altar", float.PositiveInfinity) != null ? 12 : -1);
        var sigrid = new Resident(new FakeVillager(9302, "Sigrid"));
        var leif = new Resident(new FakeVillager(9303, "Leif"));
        Assert.True(altar.TryReserve(sigrid)); // praying: the altar is full
        Assert.Equal(12f, job.PriorityFor(sigrid)); // its own turn: it keeps asking, and is not called away
        Assert.Equal(-1f, job.PriorityFor(leif));
        Assert.Null(registry.Nearest(Caller, (0, 0, 0), "altar", float.PositiveInfinity)); // asked outside a priority: full
        altar.IsEnabled = false;
        Assert.Equal(-1f, job.PriorityFor(sigrid)); // disabled: not even for the one at it
    }

    [Fact]
    public void AVillagerThatLeavesInTheMiddleLetsGoOfItsTurn()
    {
        var (run, _, altar) = Run(TimeSpan.FromSeconds(5));
        var resident = ResidentRoster.Track(new FakeVillager(9304, "Ulf"));
        run = new JobRun(run.Job, resident);
        resident.Run = run;
        Approach(run, altar, now: 0);
        run.Begin(1, now: 0);
        Assert.False(run.IsOver(0));
        Assert.Equal(1, altar.UserCount);
        ResidentRoster.Forget(resident.Key); // it died, or its world unloaded
        Assert.Null(resident.Run);
        Assert.Equal(0, altar.UserCount);
    }

    [Fact]
    public void ACancelOrAnEndCutShortReportsNoUse()
    {
        var (run, _, altar) = Run(TimeSpan.FromSeconds(5));
        int used = 0;
        altar.Used += (_, _) => used++;
        EventHandler<InteractionUsingEventArgs> cancel = (_, e) => e.Cancel = true;
        altar.Using += cancel;
        Approach(run, altar, now: 0);
        run.Begin(1, now: 0);
        Assert.True(run.IsOver(0));
        Assert.Equal(0, altar.UserCount);
        altar.Using -= cancel;

        run.Begin(1, now: 10);
        Assert.False(run.IsOver(10));
        Assert.Equal(1, altar.UserCount);
        run.End(); // another quest took the villager
        Assert.Equal(0, altar.UserCount);
        Assert.Equal(0, used);

        run.Begin(1, now: 20);
        Assert.False(run.IsOver(20));
        Assert.True(altar.Remove());
        Assert.True(run.IsOver(21));
        Assert.Equal(0, used);
    }
}
