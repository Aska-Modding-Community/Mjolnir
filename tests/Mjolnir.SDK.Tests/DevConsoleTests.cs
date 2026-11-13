using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using Mjolnir.SDK.Threading;
using Xunit;

namespace Mjolnir.SDK.Tests;

/// <summary>
/// The developer console without its window: a line's words, commands and their full names, the answers and errors it
/// shows, a usage error, a mod disabled after an error, help and clear, its newest lines kept, and a line from another
/// thread. The commands' owners are dynamic assemblies standing in for mods.
/// </summary>
[Collection("MainThread")]
public sealed class DevConsoleTests : IDisposable
{
    private static readonly Assembly ModA = Dynamic("Console.ModA");
    private static readonly Assembly ModB = Dynamic("Console.ModB");
    private readonly Func<Assembly, string> _owners = DevConsole.OwnerOf;

    public DevConsoleTests()
    {
        Dispatcher.ResetForTests();
        DevConsole.ResetForTests();
        DevConsole.OwnerOf = a => a == ModA ? "mod.a" : a == ModB ? "mod.b" : a.GetName().Name == "Console.Disabled" ? "mod.off" : _owners(a);
    }

    public void Dispose()
    {
        DevConsole.OwnerOf = _owners;
        DevConsole.ResetForTests();
        Dispatcher.ResetForTests();
    }

    [Fact]
    public void ALineIsSplitIntoWordsAndQuotesKeepSpaces()
    {
        Assert.Equal(["give", "Stone blade", "3"], DevConsole.Split("give  \"Stone blade\" 3 "));
        Assert.Equal(["say", ""], DevConsole.Split("say \"\""));
        Assert.Empty(DevConsole.Split("   "));
    }

    [Fact]
    public void ACommandGetsTheWordsAfterItsNameAndAnswers()
    {
        DevConsole.AddFor(ModA, "echo", "Says it.", c => c.Print(string.Join("|", c.Arguments)));

        DevConsole.Execute("  echo a \"b c\" ");

        Assert.Equal(["Input: echo a \"b c\"", "Output: a|b c"], Shown());
    }

    [Fact]
    public void AnUnknownCommandIsAnError()
    {
        DevConsole.Execute("nope 1");

        Assert.Equal("Error: Unknown command 'nope'. Type help for the list.", Shown()[^1]);
    }

    [Fact]
    public void ANameTakenAnswersToTheFullName()
    {
        DevConsole.AddFor(ModA, "go", "", c => c.Print("a"));
        var second = DevConsole.AddFor(ModB, "go", "", c => c.Print("b"));

        DevConsole.Execute("GO");
        DevConsole.Execute("mod.b:go");

        Assert.Equal("mod.b:go", second.FullName);
        Assert.Equal(["Output: a", "Output: b"], Shown().Where(l => l.StartsWith("Output", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheSameFullNameTwiceIsRefused()
    {
        DevConsole.AddFor(ModA, "twice", "", _ => { });

        Assert.Throws<ArgumentException>(() => DevConsole.AddFor(ModA, "TWICE", "", _ => { }));
    }

    [Theory]
    [InlineData("give", true)]
    [InlineData("tp-2", true)]
    [InlineData("set_time", true)]
    [InlineData("a b", false)]
    [InlineData("mod:go", false)]
    [InlineData("", false)]
    public void NamesAreLettersDigitsUnderscoresAndDashes(string name, bool valid) => Assert.Equal(valid, DevConsole.IsValidName(name));

    [Fact]
    public void AUsageErrorShowsTheUsageAndKeepsTheModEnabled()
    {
        DevConsole.AddFor(ModA, "count", "", c => c.Print(c.GetInt(0).ToString()), "<n>");

        DevConsole.Execute("count x");

        Assert.Equal(["Error: Argument 1 is not a whole number: 'x'.", "Error: Usage: count <n>"], Shown()[^2..]);
        Assert.False(ModCall.IsDisabled(ModA));
    }

    [Fact]
    public void ReadersTakeNumbersWithAPointOrAComma()
    {
        DevConsole.AddFor(ModA, "read", "", c => c.Print(FormattableString.Invariant($"{c.GetFloat(0)} {c.GetFloat(1)} {c.GetInt(2, 7)} {c.GetFloat(3, 0.5f)} / {c.GetRest(0)}")));

        DevConsole.Execute("read 1,5 -2.25");

        Assert.Equal("Output: 1.5 -2.25 7 0.5 / 1,5 -2.25", Shown()[^1]);
    }

    [Fact]
    public void AMissingWordIsAUsageError()
    {
        DevConsole.AddFor(ModA, "need", "", c => c.Print(c.GetString(1)), "<a> <b>");

        DevConsole.Execute("need one");

        Assert.Equal(["Error: Argument 2 is missing.", "Error: Usage: need <a> <b>"], Shown()[^2..]);
    }

    [Fact]
    public void AThrowingCommandSaysSo()
    {
        DevConsole.AddFor(ModA, "boom", "", _ => throw new InvalidOperationException("no luck"));

        DevConsole.Execute("boom");

        Assert.Equal("Error: boom failed: no luck", Shown()[^1]);
    }

    [Fact]
    public void ADisabledModsCommandDoesNotRun()
    {
        var mod = Dynamic("Console.Disabled");
        bool ran = false;
        DevConsole.AddFor(mod, "off", "", _ => ran = true);
        ModCall.Disable(mod, "test");

        DevConsole.Execute("off");

        Assert.False(ran);
        Assert.Equal("Error: mod.off:off: its mod was disabled after an error.", Shown()[^1]);
    }

    [Fact]
    public void HelpListsTheCommandsAndDescribesOne()
    {
        DevConsole.AddFor(ModA, "echo", "Says it.", _ => { }, "<text>");

        DevConsole.Execute("help");
        Assert.Contains("Output: echo <text> - Says it.", Shown());
        Assert.Contains("Output: help [command] - Lists the commands, or describes one.", Shown());

        DevConsole.Execute("help echo");
        Assert.Equal(["Output: echo <text>  (mod.a:echo)", "Output:   Says it."], Shown()[^2..]);

        DevConsole.Execute("help nothing");
        Assert.Equal(["Error: No command 'nothing'.", "Error: Usage: help [command]"], Shown()[^2..]);
    }

    [Fact]
    public void ThePlatformsCommandsAreMjolnirs()
    {
        Assert.Equal("mjolnir:help", DevConsole.Find("help")?.FullName);
        Assert.Equal("mjolnir", DevConsole.Find("clear")?.ModId);
    }

    [Fact]
    public void ClearEmptiesTheConsole()
    {
        DevConsole.Print("before");

        DevConsole.Execute("clear");

        Assert.Empty(DevConsole.Lines);
    }

    [Fact]
    public void TheConsoleKeepsItsNewestLines()
    {
        for (int i = 0; i < DevConsole.MaxLines + 100; i++) DevConsole.Print(i.ToString());

        Assert.Equal(DevConsole.MaxLines, DevConsole.Lines.Count);
        Assert.Equal((DevConsole.MaxLines + 99).ToString(), DevConsole.Lines[^1].Text);
    }

    [Fact]
    public void ALineOfSeveralLinesIsShownAsSeveral()
    {
        DevConsole.Print("a\r\nb");

        Assert.Equal(["Output: a", "Output: b"], Shown());
    }

    [Fact]
    public void ARemovedCommandIsUnknown()
    {
        var command = DevConsole.AddFor(ModA, "gone", "", _ => { });
        DevConsole.Remove(command);

        DevConsole.Execute("gone");

        Assert.Equal("Error: Unknown command 'gone'. Type help for the list.", Shown()[^1]);
    }

    [Fact]
    public void ALineFromAnotherThreadRunsOnTheNextFrame()
    {
        Dispatcher.Tick(); // this thread is now the main thread
        int ranOn = -1;
        DevConsole.AddFor(ModA, "where", "", _ => ranOn = Environment.CurrentManagedThreadId);

        var worker = new Thread(() => DevConsole.Execute("where"));
        worker.Start();
        worker.Join();
        Assert.Equal(-1, ranOn);

        Dispatcher.Tick();
        Assert.Equal(Environment.CurrentManagedThreadId, ranOn);
    }

    private static string[] Shown() => [.. DevConsole.Lines.Select(l => $"{l.Kind}: {l.Text}")];

    private static Assembly Dynamic(string name) => AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(name), AssemblyBuilderAccess.Run);
}
