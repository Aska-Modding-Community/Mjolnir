using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Mjolnir.Core.Interop;

/// <summary>
/// Keeps Mjolnir\interop for the game's build. The interop assemblies are generated on the
/// player's machine, not shipped: a stamp in the folder records what they were made for (the game's build, the
/// Il2CppInterop the runtime loads, the generator's Cpp2IL and Il2CppInterop.Generator). When it does not match, at the
/// first start after Mjolnir is installed, the game is updated or Mjolnir brings another Il2CppInterop, Core runs the
/// generator (Mjolnir\interopgen, its own process with its own progress window) in phase A, while the game waits in
/// il2cpp_init with no window of its own, and puts its output in place only once it is complete.
/// </summary>
internal static class InteropCache
{
    public const string StampFileName = "mjolnir-interop.json";
    public const string GeneratorExeName = "Mjolnir.InteropGen.exe";
    public const int CancelledExitCode = 3; // the player started the game without mods from the progress window
    public const int UnityLibsMissingExitCode = 4; // Unity's base libraries neither in the cache nor downloadable (offline)
    private const string Marker = "Il2Cppmscorlib.dll";
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(15);

    internal enum Outcome { Current, Generated, Unchecked, Cancelled, Failed, Missing }

    /// <summary>What interop assemblies are made for; a field of null is not known.</summary>
    internal sealed record Stamp(string? Game, string? Il2CppInterop, string? Generator);

    /// <summary>The folders and files involved: <see cref="For"/> gives the game's, unit tests their own.</summary>
    internal sealed record Layout(string GameDir, string InteropDir, string GeneratorDir, string DotnetRoot, string CacheDir)
    {
        public string NewDir => InteropDir + ".new";
        public string OldDir => InteropDir + ".old";
        public string StampPath => Path.Combine(InteropDir, StampFileName);
        public string GeneratorExe => Path.Combine(GeneratorDir, GeneratorExeName);
        public string LogPath => Path.Combine(CacheDir, "interop-gen.log");
        public string UnityLibsDir => Path.Combine(CacheDir, "unity-libs");

        public static Layout For(LoaderPaths paths) =>
            new(paths.GameDir, paths.InteropDir, Path.Combine(paths.LoaderDir, "interopgen"),
                Path.Combine(paths.LoaderDir, "dotnet"), paths.CacheDir);
    }

    /// <summary>
    /// Never throws. True when Mjolnir\interop is there for this build (found, or generated now); false means the game
    /// goes on without mods: generation failed, the player chose to start without them, or there is nothing at all.
    /// </summary>
    public static bool EnsureReady(LoaderPaths paths, GameIdentity? game)
    {
        try
        {
            var layout = Layout.For(paths);
            var outcome = Ensure(layout, Expected(layout, game), RunGenerator);
            return outcome is Outcome.Current or Outcome.Generated or Outcome.Unchecked;
        }
        catch (Exception e)
        {
            Log.Error("Interop", "Checking the interop assemblies failed: " + e);
            return false;
        }
    }

    internal static Stamp Expected(Layout layout, GameIdentity? game) =>
        new(game?.CacheKey,
            VersionOf(Path.Combine(Path.GetDirectoryName(typeof(InteropCache).Assembly.Location) ?? "", "Il2CppInterop.Runtime.dll")),
            GeneratorVersion(layout.GeneratorDir));

    // The generator's two libraries decide what it makes; its own exe is the same code whatever they are.
    internal static string? GeneratorVersion(string generatorDir)
    {
        string? cpp2il = VersionOf(Path.Combine(generatorDir, "Cpp2IL.Core.dll"));
        string? generator = VersionOf(Path.Combine(generatorDir, "Il2CppInterop.Generator.dll"));
        return cpp2il == null || generator == null ? null : $"Cpp2IL {cpp2il}, Il2CppInterop.Generator {generator}";
    }

    // The informational version, which tells pre-releases apart (every Cpp2IL 2022.1.0 pre-release has the assembly version
    // 2022.1.0.0), else the file version, else the assembly version.
    internal static string? VersionOf(string assembly)
    {
        try
        {
            if (!File.Exists(assembly)) return null;
            var info = FileVersionInfo.GetVersionInfo(assembly);
            if (!string.IsNullOrWhiteSpace(info.ProductVersion)) return info.ProductVersion.Trim();
            if (!string.IsNullOrWhiteSpace(info.FileVersion)) return info.FileVersion.Trim();
            return AssemblyName.GetAssemblyName(assembly).Version?.ToString();
        }
        catch (Exception e) when (e is IOException or BadImageFormatException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>The decision and the swap, with the generator's run given (a fake in unit tests); returns what happened.</summary>
    internal static Outcome Ensure(Layout layout, Stamp expected, Func<Layout, int> run)
    {
        bool present = File.Exists(Path.Combine(layout.InteropDir, Marker));
        var stamp = ReadStamp(layout.StampPath);
        if (present && stamp != null && IsCurrent(stamp, expected)) return Outcome.Current;

        if (expected.Game == null || !File.Exists(layout.GeneratorExe))
        {
            // Without the game's identity nothing can be compared, without the generator nothing can be made: what is
            // there is used as it is (a developer's deploy, an install missing its generator).
            string why = expected.Game == null ? "the game's build is unknown" : $"no generator in {layout.GeneratorDir}";
            if (!present)
            {
                Log.Error("Interop", $"No interop assemblies in {layout.InteropDir} and {why}: the game runs without mods");
                return Outcome.Missing;
            }
            Log.Warn("Interop", $"Interop assemblies in {layout.InteropDir} used unchecked: {why}");
            return Outcome.Unchecked;
        }

        Log.Info("Interop", present
            ? $"Interop assemblies are for {Describe(stamp)}, this is {Describe(expected)}: generating them again"
            : $"No interop assemblies yet: generating them for {Describe(expected)}");
        DeleteDir(layout.NewDir);
        var watch = Stopwatch.StartNew();
        int code = run(layout);
        if (code == 0 && File.Exists(Path.Combine(layout.NewDir, Marker)))
        {
            Swap(layout);
            WriteStamp(layout.StampPath, expected);
            Log.Info("Interop", $"Interop assemblies generated in {watch.Elapsed.TotalSeconds:F1} s " +
                                $"({Directory.GetFiles(layout.InteropDir, "*.dll").Length} assemblies; log: {layout.LogPath})");
            return Outcome.Generated;
        }
        DeleteDir(layout.NewDir);
        if (code == CancelledExitCode)
        {
            Log.Info("Interop", "The player started the game without mods; the interop assemblies are generated at the next start");
            return Outcome.Cancelled;
        }
        if (code == UnityLibsMissingExitCode)
        {
            Log.Error("Interop", $"Unity's base libraries could not be downloaded into {layout.UnityLibsDir} (no connection?): the " +
                                 "game runs without mods, and the generation is tried again at the next start");
            return Outcome.Failed;
        }
        Log.Error("Interop", $"Generating the interop assemblies failed (exit code {code}, log: {layout.LogPath}): the game runs " +
                             "without mods, and the generation is tried again at the next start");
        return Outcome.Failed;
    }

    internal static bool IsCurrent(Stamp found, Stamp expected) =>
        found.Game == expected.Game && found.Il2CppInterop == expected.Il2CppInterop && found.Generator == expected.Generator;

    private static string Describe(Stamp? s) =>
        s == null ? "an unknown build" : $"game {s.Game ?? "?"}, Il2CppInterop {s.Il2CppInterop ?? "?"}, {s.Generator ?? "unknown generator"}";

    // The new folder takes the old one's place; the old one is put back if that fails.
    internal static void Swap(Layout layout)
    {
        DeleteDir(layout.OldDir);
        bool hadOld = Directory.Exists(layout.InteropDir);
        if (hadOld) Directory.Move(layout.InteropDir, layout.OldDir);
        try
        {
            Directory.Move(layout.NewDir, layout.InteropDir);
        }
        catch
        {
            if (hadOld && !Directory.Exists(layout.InteropDir)) Directory.Move(layout.OldDir, layout.InteropDir);
            throw;
        }
        try { DeleteDir(layout.OldDir); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Interop", $"Could not remove {layout.OldDir}: {e.Message}");
        }
    }

    internal static Stamp? ReadStamp(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<Stamp>(File.ReadAllText(path)) : null; }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    internal static void WriteStamp(string path, Stamp stamp)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(stamp, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Interop", $"Could not write {path}: {e.Message}; the assemblies are generated again at the next start");
        }
    }

    private static void DeleteDir(string dir)
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    /// <summary>The generator's arguments; its own window shows while it runs.</summary>
    internal static IReadOnlyList<string> Arguments(Layout layout) =>
        ["--game", layout.GameDir, "--out", layout.NewDir, "--unity-libs-cache", layout.UnityLibsDir, "--window"];

    // Runs the generator on the loader's private .NET (DOTNET_ROOT: the player may have no .NET installed) and waits,
    // its output in cache\interop-gen.log and its last lines in LogOutput.log when it fails.
    private static int RunGenerator(Layout layout)
    {
        Directory.CreateDirectory(layout.CacheDir);
        var psi = new ProcessStartInfo(layout.GeneratorExe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = layout.GeneratorDir,
        };
        foreach (string arg in Arguments(layout)) psi.ArgumentList.Add(arg);
        if (Directory.Exists(layout.DotnetRoot))
        {
            psi.Environment["DOTNET_ROOT"] = layout.DotnetRoot;
            psi.Environment["DOTNET_ROOT_X64"] = layout.DotnetRoot;
        }

        var last = new Queue<string>();
        using var log = new StreamWriter(layout.LogPath, append: false);
        void Line(string? line)
        {
            if (line == null) return;
            lock (log)
            {
                log.WriteLine(line);
                last.Enqueue(line);
                if (last.Count > 12) last.Dequeue();
            }
        }

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => Line(e.Data);
        process.ErrorDataReceived += (_, e) => Line(e.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (!process.WaitForExit(Timeout))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            Log.Error("Interop", $"The generator ran longer than {Timeout.TotalMinutes} minutes and was stopped");
            return -1;
        }
        process.WaitForExit(); // the output's last lines
        int code = process.ExitCode;
        if (code != 0 && code != CancelledExitCode && code != UnityLibsMissingExitCode)
            lock (log) Log.Error("Interop", "The generator's last lines:\n  " + string.Join("\n  ", last.Select(l => l.Length > 300 ? l[..300] : l)));
        return code;
    }
}
