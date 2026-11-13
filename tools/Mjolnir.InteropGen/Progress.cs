using System;
using System.Diagnostics;
using System.Linq;

namespace Mjolnir.InteropGen;

/// <summary>What the generation is doing, for the progress window.</summary>
internal enum Stage
{
    Starting,
    Download,   // Unity's base libraries, once per Unity version
    ReadCode,   // Cpp2IL: GameAssembly.dll and global-metadata.dat
    Types,      // Cpp2IL: dummy assemblies
    Assemblies, // Il2CppInterop.Generator
    Verify,
}

/// <summary>
/// How far the generation is, from 0 to 1. Each step has its share of the whole by how long it took when measured
/// (2026-11-07, 30 s in all), so a slower machine moves through the same fractions. Within a
/// step the fraction grows with the step's time and stops short of the next step; a step that is not in the table (a
/// generator of another version) leaves the fraction where it is.
/// </summary>
internal static class Progress
{
    private static readonly (string Step, double Seconds)[] Steps =
    [
        ("cpp2il: read", 4.6), ("cpp2il: types", 6.5),
        ("Reading assemblies", 0.01), ("Reading unity assemblies", 0.17), ("Creating rewrite assemblies", 0.01),
        ("Computing renames", 0.18), ("Creating typedefs", 0.47), ("Computing struct blittability", 0.08),
        ("Filling typedefs", 0.53), ("Filling generic constraints", 0.03), ("Creating members", 1.14),
        ("Scanning method cross-references", 0.92), ("Finalizing method declarations", 2.21),
        ("Filling method parameters", 0.4), ("Creating static constructors", 0.96), ("Creating value type fields", 0.4),
        ("Creating enums", 0.04), ("Creating IntPtr constructors", 0.15), ("Creating non-blittable struct constructors", 0.07),
        ("Creating generic method static constructors", 0.02), ("Creating field accessors", 0.07), ("Filling methods", 1.53),
        ("Generating implicit conversions", 1.22), ("Implementing awaiters", 0.04), ("Creating properties", 0.02),
        ("Unstripping types", 0.52), ("Unstripping fields", 0.17), ("Unstripping methods", 0.05),
        ("Unstripping method bodies", 2.6), ("Writing xref cache", 0.75), ("Writing assemblies", 4.62),
        ("Writing method pointer map", 0.08), ("verify", 0.5),
    ];

    private static readonly double Total = Steps.Sum(s => s.Seconds);
    private static readonly object Gate = new();
    private static readonly Stopwatch StepClock = new();
    private static int s_step = -1;

    public static Stage Stage { get; private set; } = Stage.Starting;

    public static void Set(Stage stage)
    {
        lock (Gate) Stage = stage;
    }

    /// <summary>A step begins: one of the table's names, or an Il2CppInterop message such as "Creating members...".</summary>
    public static void Begin(string step)
    {
        string name = step.TrimEnd('.', ' ');
        int index = Array.FindIndex(Steps, s => string.Equals(s.Step, name, StringComparison.OrdinalIgnoreCase));
        lock (Gate)
        {
            if (index <= s_step) return;
            s_step = index;
            StepClock.Restart();
            Stage = index == 0 ? Stage.ReadCode : index == 1 ? Stage.Types : index == Steps.Length - 1 ? Stage.Verify : Stage.Assemblies;
        }
    }

    public static double Fraction
    {
        get
        {
            lock (Gate)
            {
                if (s_step < 0) return 0;
                double done = Steps.Take(s_step).Sum(s => s.Seconds);
                double share = Steps[s_step].Seconds;
                double within = Math.Min(StepClock.Elapsed.TotalSeconds / share, 0.95);
                return Math.Min((done + within * share) / Total, 1);
            }
        }
    }
}
