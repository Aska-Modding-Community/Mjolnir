using System;
using Mjolnir.SDK;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Mjolnir.SDK.AI.Tests;

/// <summary>A villager of the game as the model sees it, set by the test.</summary>
internal sealed class FakeVillager(nint key, string name) : IVillagerSource
{
    public nint Key { get; } = key;
    public bool IsAlive { get; set; } = true;
    public string Name { get; set; } = name;
    public (float X, float Y, float Z) Position { get; set; }
    public string Activity { get; set; } = "";
    public (float X, float Y, float Z)? Faced { get; private set; }

    public void Face((float X, float Y, float Z) place) => Faced = place;
}

/// <summary>
/// The world's villagers as residents (one class: the table is the module's, shared by the process): one resident per
/// villager, announced once when it comes and once when it goes, what it shows after it left, and what it does.
/// </summary>
public class ResidentsTests
{
    private static readonly Assembly Caller = typeof(ResidentsTests).Assembly;

    [Fact]
    public void AVillagerComesAndGoesOnce()
    {
        var added = new List<string>();
        var removed = new List<string>();
        EventHandler<ResidentEventArgs> onAdded = (_, e) => added.Add(e.Resident.Name);
        EventHandler<ResidentEventArgs> onRemoved = (_, e) => removed.Add(e.Resident.Name);
        Residents.Added += onAdded;
        Residents.Removed += onRemoved;
        try
        {
            var ragna = new FakeVillager(9001, "Ragna");
            var first = ResidentRoster.Track(ragna);
            Assert.Same(first, ResidentRoster.Track(new FakeVillager(9001, "Ragna"))); // the game wraps a villager anew each time
            Assert.Same(first, ResidentRoster.Find(9001));
            Assert.Contains(first, Residents.All);
            ResidentRoster.Forget(9001);
            ResidentRoster.Forget(9001);
            Assert.Null(ResidentRoster.Find(9001));
            Assert.DoesNotContain(first, Residents.All);
            Assert.Equal(["Ragna"], added);
            Assert.Equal(["Ragna"], removed);
        }
        finally
        {
            Residents.Added -= onAdded;
            Residents.Removed -= onRemoved;
        }
    }

    [Fact]
    public void AVillagerWhoseWorldUnloadedIsSweptAndKeepsItsLastNameAndPlace()
    {
        var bjorn = new FakeVillager(9002, "Bjorn") { Position = (1, 2, 3), Activity = "ComplainQuest" };
        var resident = ResidentRoster.Track(bjorn);
        Assert.Equal(("Bjorn", "ComplainQuest", (1f, 2f, 3f)), (resident.Name, resident.Activity, resident.PositionXyz));
        bjorn.IsAlive = false;
        bjorn.Name = "";
        Assert.Contains((nint)9002, ResidentRoster.Sweep()); // the game side forgets its quests too
        Assert.Null(ResidentRoster.Find(9002));
        Assert.False(resident.IsAlive);
        Assert.Equal(("Bjorn", "", (1f, 2f, 3f)), (resident.Name, resident.Activity, resident.PositionXyz));
    }

    [Fact]
    public void AResidentRunningAJobShowsTheJob()
    {
        var registry = new JobRegistry(_ => "mjolnir.test");
        var job = registry.Add(Caller, "greet", _ => 1).Wait(TimeSpan.FromSeconds(1));
        var resident = new Resident(new FakeVillager(9003, "Sigrid") { Activity = "MjolnirQuest" });
        Assert.Null(resident.ModJob);
        resident.Run = new JobRun(job, resident);
        Assert.Same(job, resident.ModJob);
        Assert.Equal("mjolnir.test.greet", resident.Activity);
    }
}

/// <summary>The jobs mods add: names, owners, steps in order, and the priority the game asks.</summary>
public class JobsTests
{
    private static readonly Assembly Caller = typeof(JobsTests).Assembly;

    private static JobRegistry Registry(string? modId = "mjolnir.test") => new(_ => modId);

    [Fact]
    public void AJobKeepsItsStepsInOrder()
    {
        var registry = Registry();
        var job = registry.Add(Caller, "greet", _ => 2);
        Assert.Equal(("mjolnir.test", "greet", "mjolnir.test.greet"), (job.ModId, job.Name, job.ToString()));
        Assert.Empty(job.Steps);
        Assert.Same(job, job.MoveTo(_ => default, 3).Wait(TimeSpan.FromSeconds(2)).Do(_ => { }));
        Assert.Collection(job.Steps,
            s => Assert.Equal(3f, Assert.IsType<MoveStep>(s).Distance),
            s => Assert.Equal(TimeSpan.FromSeconds(2), Assert.IsType<WaitStep>(s).Duration),
            s => Assert.IsType<DoStep>(s));
        Assert.Equal(3, job.Version); // each step: the game makes the job's state machine again
        var steps = job.Steps;
        Assert.Same(steps, job.Steps); // read every second for every villager: not copied each time
        job.Use(_ => null, TimeSpan.FromSeconds(1)); // its walk and its turn come as one change
        Assert.Equal((4, 5), (job.Version, job.Steps.Count));
        Assert.Equal(3, steps.Count);
        Assert.Same(Caller, job.Owner);
    }

    [Fact]
    public void BadJobsAndStepsAreRefused()
    {
        var registry = Registry();
        var job = registry.Add(Caller, "greet", _ => 1);
        Assert.Throws<ArgumentException>(() => registry.Add(Caller, "greet", _ => 1));
        Assert.Throws<ArgumentException>(() => registry.Add(Caller, "say hi", _ => 1));
        Assert.Throws<ArgumentNullException>(() => registry.Add(Caller, "other", null!));
        Assert.Throws<InvalidOperationException>(() => Registry(null).Add(Caller, "greet", _ => 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => job.MoveTo(_ => default, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => job.MoveTo(_ => default, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => job.Wait(TimeSpan.FromSeconds(-1)));
        Assert.Throws<ArgumentNullException>(() => job.Do(null!));
        Assert.Empty(job.Steps);
        Assert.Equal(["mjolnir.test.greet"], registry.Jobs.Select(j => j.ToString()));
    }

    [Fact]
    public void ThePriorityIsTheModsAndNotANumberIsMinusOne()
    {
        var registry = Registry();
        var resident = new Resident(new FakeVillager(9101, "Astrid"));
        Assert.Equal(2.5f, registry.Add(Caller, "eager", r => r.Name == "Astrid" ? 2.5f : -1).PriorityFor(resident));
        Assert.Equal(-1f, registry.Add(Caller, "lost", _ => float.NaN).PriorityFor(resident));
    }

    [Fact]
    public void ModsKeepTheirOwnNames()
    {
        string modId = "mjolnir.a";
        var registry = new JobRegistry(_ => modId);
        var a = registry.Add(Caller, "greet", _ => 1);
        modId = "mjolnir.b";
        var b = registry.Add(Caller, "greet", _ => 1);
        Assert.Equal(["mjolnir.a.greet", "mjolnir.b.greet"], registry.Jobs.Select(j => j.ToString()));
        Assert.NotSame(a, b);
    }
}

/// <summary>A villager running a job's steps: when each is over.</summary>
public class JobRunTests
{
    private static readonly Assembly Caller = typeof(JobRunTests).Assembly;

    private static (JobRun Run, FakeVillager Villager) Run(Action<ResidentJob> steps)
    {
        var job = new JobRegistry(_ => "mjolnir.test").Add(Caller, "job", _ => 1);
        steps(job);
        var villager = new FakeVillager(9201, "Leif");
        return (new JobRun(job, new Resident(villager)), villager);
    }

    [Fact]
    public void AWaitIsOverAfterItsDuration()
    {
        var (run, _) = Run(j => j.Wait(TimeSpan.FromSeconds(3)));
        Assert.Equal(-1, run.Index);
        run.Begin(0, now: 10);
        Assert.False(run.IsOver(12.9));
        Assert.True(run.IsOver(13));
    }

    [Fact]
    public void APlayIsOverWhenItsAnimationEndsOrItsDurationIsUp()
    {
        var wave = new AnimationInfo("aska_wave", null, 3);
        var (run, _) = Run(j => j.Play(wave).Play(wave, duration: TimeSpan.FromSeconds(5)));
        run.Begin(0, now: 0);
        run.Playback = CharacterAnimator.Of(0).Play(wave); // no character here: it ends at the next tick
        Assert.False(run.IsOver(1));
        Animations.Tick();
        Assert.True(run.IsOver(1));

        run.Begin(1, now: 10);
        Assert.False(run.IsOver(14.9));
        Assert.True(run.IsOver(15));
        Assert.Throws<ArgumentOutOfRangeException>(() => Run(j => j.Play(wave, duration: TimeSpan.FromSeconds(-1))));
    }

    [Fact]
    public void AWalkIsOverWithinItsDistanceAcrossTheGround()
    {
        var (run, villager) = Run(j => j.MoveTo(_ => default, 2));
        run.Begin(0, now: 0);
        Assert.True(run.IsOver(1)); // no place known (the mod's function threw): nothing to walk to
        run.Target = (10, 0, 0);
        villager.Position = (7, 0, 0);
        Assert.False(run.IsOver(1));
        villager.Position = (8.5f, 5, 0); // a slope: only across the ground counts
        Assert.True(run.IsOver(1));
    }

    [Fact]
    public void AWalkIsOverWhenTheVillagerStopsCloseToItsPlace()
    {
        var (run, villager) = Run(j => j.MoveTo(_ => default, 3));
        villager.Position = (0, 0, 0);
        run.Begin(0, now: 0);
        run.Target = (10, 0, 0);
        villager.Position = (6.5f, 0, 0); // 3.5 m away: others stand in the way
        Assert.False(run.IsOver(1)); // it moved since the step began
        Assert.False(run.IsOver(3.9));
        Assert.True(run.IsOver(4)); // still for 3 s, within its distance and 3 m more
        villager.Position = (2, 0, 0);
        run.Begin(0, now: 10);
        run.Target = (10, 0, 0); // 8 m away: stopped too far to count
        Assert.False(run.IsOver(20));
    }

    [Fact]
    public void AWalkThatNeverArrivesMovesOnAfterAMinute()
    {
        var (run, villager) = Run(j => j.MoveTo(_ => default));
        run.Begin(0, now: 100);
        run.Target = (50, 0, 50);
        Assert.False(run.IsOver(159));
        Assert.True(run.IsOver(100 + JobRun.MoveTimeout.TotalSeconds));
        run.Begin(0, now: 200);
        Assert.Null(run.Target); // a new step forgets the last place
        _ = villager;
    }

    [Fact]
    public void CodeIsOverAtOnceAndTheRunKeepsItsSteps()
    {
        var (run, _) = Run(j => j.Do(_ => { }).Wait(TimeSpan.Zero));
        run.Begin(0, now: 5);
        Assert.IsType<DoStep>(run.Step);
        Assert.True(run.IsOver(5));
        run.Job.Wait(TimeSpan.FromSeconds(1)); // a step added later reaches the next run, not this one
        Assert.Equal(2, run.Steps.Count);
        run.Begin(1, now: 6);
        Assert.True(run.IsOver(6));
    }
}
