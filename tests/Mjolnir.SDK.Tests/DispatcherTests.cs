using System;
using System.Collections.Generic;
using System.Threading;
using Mjolnir.SDK.Threading;
using Xunit;

namespace Mjolnir.SDK.Tests;

/// <summary>The Dispatcher's main thread is process-wide: the classes that use it run one after another.</summary>
[CollectionDefinition("MainThread", DisableParallelization = true)]
public sealed class MainThreadCollection;

/// <summary>The SDK's Dispatcher: the main thread, posting to it, and SDK code run from other threads.</summary>
[Collection("MainThread")]
public sealed class DispatcherTests : IDisposable
{
    public DispatcherTests() => Dispatcher.ResetForTests();

    public void Dispose() => Dispatcher.ResetForTests();

    [Fact]
    public void PostedActionsRunOnTheNextTickInOrder()
    {
        var ran = new List<int>();
        Dispatcher.MainThread.Post(() => ran.Add(1));
        Dispatcher.MainThread.Post(() => ran.Add(2));
        Assert.Empty(ran);

        Dispatcher.Tick();
        Assert.Equal([1, 2], ran);
    }

    [Fact]
    public void AnActionThatPostsAgainRunsOnTheFollowingTick()
    {
        int ran = 0;
        void Again()
        {
            ran++;
            Dispatcher.MainThread.Post(Again);
        }

        Dispatcher.MainThread.Post(Again);
        Dispatcher.Tick();
        Assert.Equal(1, ran);
        Dispatcher.Tick();
        Assert.Equal(2, ran);
    }

    [Fact]
    public void SdkCodeFromAnotherThreadWaitsForTheMainThread()
    {
        Dispatcher.Tick(); // this thread is now the main thread
        Assert.True(Dispatcher.MainThread.CheckAccess());
        int ran = 0;
        bool access = true;

        var worker = new Thread(() =>
        {
            access = Dispatcher.MainThread.CheckAccess();
            Dispatcher.Run(() => ran++);
        });
        worker.Start();
        worker.Join();
        Assert.False(access);
        Assert.Equal(0, ran);
        Assert.Throws<InvalidOperationException>(() => RunOn(Dispatcher.MainThread.VerifyAccess));

        Dispatcher.Tick();
        Assert.Equal(1, ran);
    }

    [Fact]
    public void BeforeTheGameIsReadyEveryThreadIsTheMainThread()
    {
        bool access = false;
        RunOn(() => access = Dispatcher.MainThread.CheckAccess());
        Assert.True(access);
    }

    // Runs on a real thread (Task.Wait may run a task inline on the waiting thread) and rethrows its exception here.
    private static void RunOn(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                error = e;
            }
        });
        thread.Start();
        thread.Join();
        if (error != null) throw error;
    }

    [Fact]
    public void TheMainThreadsContextPostsToTheNextTickAndSendsInlineOnTheMainThread()
    {
        Dispatcher.Tick(); // this thread is now the main thread
        var context = new DispatcherSynchronizationContext();
        var seen = new List<string>();
        context.Post(_ => seen.Add("posted"), null);
        context.Send(_ => seen.Add("sent"), null);
        Assert.Equal(["sent"], seen);

        Dispatcher.Tick();
        Assert.Equal(["sent", "posted"], seen);
        Assert.Same(context, context.CreateCopy());
    }
}
