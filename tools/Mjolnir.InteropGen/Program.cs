using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using Cpp2IL.Core;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.OutputFormats;
using Cpp2IL.Core.ProcessingLayers;
using Il2CppInterop.Common;
using Il2CppInterop.Generator;
using Il2CppInterop.Generator.Runners;
using Microsoft.Extensions.Logging;

namespace Mjolnir.InteropGen;

/// <summary>
/// Step 0: GameAssembly.dll + global-metadata.dat → Cpp2IL dummy assemblies →
/// Il2CppInterop interop assemblies. Mirrors BepInEx's in-process pipeline (Il2CppInteropManager).
/// Usage: Mjolnir.InteropGen --game &lt;dir&gt; --out &lt;dir&gt; [--unity-libs &lt;dir&gt; | --unity-libs-cache &lt;dir&gt;] [--dummy &lt;dir&gt;]
///                            [--no-method-refs] [--window]
///        Mjolnir.InteropGen --verify &lt;interop dir&gt;
///        Mjolnir.InteropGen --types &lt;assemblies dir&gt; &lt;full-name regex&gt;     (game API lookup)
///        Mjolnir.InteropGen --members &lt;assemblies dir&gt; &lt;full type name&gt;
/// For game API lookups prefer the Cpp2IL dummy assemblies (build/interop-dummy): they keep the game's
/// original shape (interfaces, enums, properties, events); the interop assemblies are what mods compile against.
/// The loader runs it at the game's start when its interop assemblies are not for the game's build (Mjolnir.Core's
/// InteropCache) with --unity-libs-cache (Unity's libraries found or downloaded there) and --window (the progress window,
/// <see cref="ProgressWindow"/>). Exit codes: 0 done, 1 failed, 2 bad arguments, 3 the player started the game without mods,
/// 4 Unity's base libraries were asked for with --unity-libs-cache and could be neither found nor downloaded (the loader then
/// runs the game without mods and tries again at the next start: assemblies made without them lack the engine's stripped
/// methods, which the SDK is built against, and would count as current for good).
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--verify")
                return Verify(Path.GetFullPath(args[1])) ? 0 : 1;
            if (args.Length == 3 && args[0] == "--types")
                return ListTypes(Path.GetFullPath(args[1]), new Regex(args[2], RegexOptions.IgnoreCase));
            if (args.Length == 3 && args[0] == "--members")
                return ListMembers(Path.GetFullPath(args[1]), args[2]);

            var opts = Options.Parse(args);
            return opts.Window ? ProgressWindow.Run(() => Generate(opts)) : Generate(opts);
        }
        catch (ArgumentException e)
        {
            Console.Error.WriteLine(e.Message);
            Console.Error.WriteLine("usage: Mjolnir.InteropGen --game <dir> --out <dir> [--unity-libs <dir> | --unity-libs-cache <dir>] [--dummy <dir>] [--no-method-refs] [--window]");
            Console.Error.WriteLine("       Mjolnir.InteropGen --verify <interop dir>");
            Console.Error.WriteLine("       Mjolnir.InteropGen --types <assemblies dir> <full-name regex>");
            Console.Error.WriteLine("       Mjolnir.InteropGen --members <assemblies dir> <full type name>");
            return 2;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("FAILED: " + e);
            return 1;
        }
    }

    /// <summary>Unity's base libraries were asked for and are not there; Mjolnir.Core's InteropCache knows the code too.</summary>
    internal const int UnityLibsMissingExitCode = 4;

    private static int Generate(Options o)
    {
        try
        {
            Run(o);
            return 0;
        }
        catch (UnityLibsMissingException e)
        {
            Console.Error.WriteLine("FAILED: " + e.Message);
            return UnityLibsMissingExitCode;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("FAILED: " + e);
            return 1;
        }
    }

    private static void Run(Options o)
    {
        var total = Stopwatch.StartNew();
        string gameAssembly = Path.Combine(o.GameDir, "GameAssembly.dll");
        string dataDir = FindDataDir(o.GameDir);
        string metadata = Path.Combine(dataDir, "il2cpp_data", "Metadata", "global-metadata.dat");
        string unityPlayer = Path.Combine(o.GameDir, "UnityPlayer.dll");
        foreach (var f in new[] { gameAssembly, metadata, unityPlayer })
            if (!File.Exists(f)) throw new FileNotFoundException("required game file not found", f);

        var unityVersion = Cpp2IlApi.DetermineUnityVersion(unityPlayer, dataDir);
        Info($"Game: {o.GameDir}");
        Info($"Unity version: {unityVersion}");
        string? unityLibs = o.UnityLibsDir ?? (o.UnityLibsCache != null ? UnityLibs.Find(o.UnityLibsCache, o.GameDir, Info) : null);
        if (unityLibs == null && o.UnityLibsCache != null)
            throw new UnityLibsMissingException($"Unity's base libraries for {unityVersion} are not in {o.UnityLibsCache} and " +
                                                "could not be downloaded; nothing is generated without them");

        // --- Cpp2IL: dummy assemblies (signatures only, stub bodies) ---
        var phase = Stopwatch.StartNew();
        Progress.Begin("cpp2il: read");
        // Registers the built-in core plugin (instruction sets such as x86_64). Cpp2IL would also
        // Assembly.LoadFrom every DLL in the plugins folder, so point it at one we never create.
        Cpp2IlApi.Init(Path.Combine(AppContext.BaseDirectory, "cpp2il-plugins"));
        Cpp2IlApi.InitializeLibCpp2Il(gameAssembly, metadata, unityVersion, false);
        Info($"Cpp2IL: metadata and binary loaded ({phase.Elapsed.TotalSeconds:F1} s)");

        phase.Restart();
        Progress.Begin("cpp2il: types");
        var layers = new List<Cpp2IlProcessingLayer> { new AttributeInjectorProcessingLayer() };
        foreach (var layer in layers) layer.PreProcess(Cpp2IlApi.CurrentAppContext!, layers);
        foreach (var layer in layers) layer.Process(Cpp2IlApi.CurrentAppContext!, (_, _) => { });
        List<AssemblyDefinition> dummies = new AsmResolverDllOutputFormatDefault().BuildAssemblies(Cpp2IlApi.CurrentAppContext!);
        Cpp2IlApi.ResetInternalState();
        Info($"Cpp2IL: {dummies.Count} dummy assemblies built ({phase.Elapsed.TotalSeconds:F1} s)");

        if (o.DummyDir != null)
        {
            Directory.CreateDirectory(o.DummyDir);
            foreach (var asm in dummies) asm.Write(Path.Combine(o.DummyDir, asm.Name + ".dll"));
            Info($"Dummy assemblies written to {o.DummyDir}");
        }

        // --- Il2CppInterop: interop assemblies ---
        phase.Restart();
        Directory.CreateDirectory(o.OutDir);
        var generatorOptions = new GeneratorOptions
        {
            GameAssemblyPath = o.ScanMethodRefs ? gameAssembly : null,  // enables method xref scanning
            Source = dummies,
            OutputDir = o.OutDir,
            UnityBaseLibsDir = unityLibs,
            Parallel = true,
        };
        Il2CppInteropGenerator.Create(generatorOptions)
            .AddLogger(new ConsoleLogger())
            .AddInteropAssemblyGenerator()
            .Run();
        Info($"Il2CppInterop: generation finished ({phase.Elapsed.TotalSeconds:F1} s)");

        var outputs = new DirectoryInfo(o.OutDir).GetFiles("*.dll");
        Info($"Output: {outputs.Length} assemblies, {outputs.Sum(f => f.Length) / 1048576.0:F1} MB in {o.OutDir}");
        Info($"Total: {total.Elapsed.TotalSeconds:F1} s");

        Progress.Begin("verify");
        if (!Verify(o.OutDir)) throw new InvalidOperationException("interop output is missing the expected game types");
    }

    /// <summary>
    /// Step 0 acceptance check: Assembly-CSharp.dll exposes SSSGame.* with PlayerCharacter and the
    /// IStaminaUser stamina API. Returns false if either type is missing.
    /// </summary>
    private static bool Verify(string interopDir)
    {
        string path = Path.Combine(interopDir, "Assembly-CSharp.dll");
        if (!File.Exists(path))
        {
            Info($"VERIFY: {path} not found");
            return false;
        }

        var module = ModuleDefinition.FromFile(path);
        var types = module.GetAllTypes().ToList();
        var gameNamespaces = types.Select(t => t.Namespace?.Value ?? "")
            .Where(n => n.Contains("SSSGame")).Distinct().OrderBy(n => n).ToList();
        Info($"VERIFY: Assembly-CSharp.dll has {types.Count} types, {gameNamespaces.Count} SSSGame namespaces " +
             $"(e.g. {string.Join(", ", gameNamespaces.Take(8))})");

        var player = types.Where(t => t.Name == "PlayerCharacter").ToList();
        foreach (var t in player)
            Info($"VERIFY: {t.FullName} : {t.BaseType?.FullName}; interfaces: " +
                 $"{string.Join(", ", t.Interfaces.Select(i => i.Interface?.Name).Take(12))}; {t.Methods.Count} methods");

        var stamina = types.Where(t => t.Name == "IStaminaUser").ToList();
        foreach (var t in stamina)
            foreach (var m in t.Methods.OrderBy(m => m.Name?.Value))
                Info($"VERIFY: {t.FullName}.{m.Name}({string.Join(", ", m.Signature!.ParameterTypes.Select(p => p.Name))}) -> {m.Signature.ReturnType.Name}");

        // Where the stamina API the SDK plans around actually lives.
        string[] staminaApi = { "TryDrainStamina", "DrainStamina", "ReduceStamina", "ChangeStamina", "ChangeMaxStamina",
                                "HaltStaminaRegen", "LockStamina", "CanUseStamina" };
        foreach (var name in staminaApi)
        {
            var owners = types.Where(t => t.Methods.Any(m => m.Name == name)).Select(t => t.FullName).Take(8).ToList();
            Info($"VERIFY: {name}: {(owners.Count == 0 ? "not found" : string.Join(", ", owners))}");
        }

        bool ok = player.Count > 0 && stamina.Count > 0;
        Info(ok ? "VERIFY: OK — PlayerCharacter and IStaminaUser found" : "VERIFY: FAILED — PlayerCharacter or IStaminaUser missing");
        return ok;
    }

    // ---- Game API lookup modes ----

    private static IEnumerable<(string Assembly, TypeDefinition Type)> AllTypes(string dir) =>
        Directory.EnumerateFiles(dir, "*.dll").OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .SelectMany(f => ModuleDefinition.FromFile(f).GetAllTypes()
                .Select(t => (Path.GetFileNameWithoutExtension(f), t)));

    private static string Kind(TypeDefinition t) =>
        t.IsInterface ? "interface"
        : t.IsEnum ? "enum"
        : t.IsValueType ? "struct"
        : t.BaseType?.FullName == "System.MulticastDelegate" ? "delegate"
        : t.IsAbstract && t.IsSealed ? "static class"
        : t.IsAbstract ? "abstract class" : "class";

    // Il2CppInterop plumbing that is not part of the game's API.
    private static bool IsPlumbing(string name) =>
        name.StartsWith("NativeFieldInfoPtr_") || name.StartsWith("NativeMethodInfoPtr_") ||
        name.StartsWith("NativePropertyInfoPtr_") || name.StartsWith("NativeEventInfoPtr_") ||
        name.StartsWith("<") || name == "NativeClassPtr";

    private static int ListTypes(string dir, Regex fullName)
    {
        int n = 0;
        foreach (var (asm, t) in AllTypes(dir).Where(x => fullName.IsMatch(x.Type.FullName)))
        {
            n++;
            string interfaces = string.Join(", ", t.Interfaces.Select(i => i.Interface?.FullName));
            Console.WriteLine($"{asm} | {Kind(t)} | {t.FullName}" +
                              (t.BaseType != null && !t.IsEnum && !t.IsValueType ? $" : {t.BaseType.FullName}" : "") +
                              (interfaces.Length > 0 ? $" | implements {interfaces}" : "") +
                              $" | F{t.Fields.Count} P{t.Properties.Count} E{t.Events.Count} M{t.Methods.Count}");
        }
        Console.WriteLine($"{n} type(s)");
        return n > 0 ? 0 : 1;
    }

    private static int ListMembers(string dir, string fullName)
    {
        var matches = AllTypes(dir).Where(x => x.Type.FullName == fullName || x.Type.FullName.Replace('/', '+') == fullName).ToList();
        if (matches.Count == 0)
        {
            Console.WriteLine($"type {fullName} not found in {dir}");
            return 1;
        }
        foreach (var (asm, t) in matches)
        {
            Console.WriteLine($"== {Kind(t)} {t.FullName} [{asm}]" + (t.BaseType != null && !t.IsEnum ? $" : {t.BaseType.FullName}" : ""));
            foreach (var i in t.Interfaces) Console.WriteLine($"   implements {i.Interface?.FullName}");

            if (t.IsEnum)
            {
                foreach (var f in t.Fields.Where(f => f.IsStatic))
                    Console.WriteLine($"   {f.Name} = {f.Constant?.InterpretData()}");
                continue;
            }
            foreach (var f in t.Fields.Where(f => !IsPlumbing(f.Name!)))
                Console.WriteLine($"   field    {Access(f.IsPublic, f.IsFamily, f.IsAssembly)}{(f.IsStatic ? " static" : "")} {Ty(f.Signature?.FieldType)} {f.Name}");
            foreach (var p in t.Properties.Where(p => !IsPlumbing(p.Name!)))
                Console.WriteLine($"   property {Ty(p.Signature?.ReturnType)} {p.Name} {{{(p.GetMethod != null ? " get;" : "")}{(p.SetMethod != null ? " set;" : "")} }}");
            foreach (var e in t.Events)
                Console.WriteLine($"   event    {e.EventType?.FullName} {e.Name}");
            foreach (var m in t.Methods.Where(m => !IsPlumbing(m.Name!) && !m.IsGetMethod && !m.IsSetMethod && !m.IsAddMethod && !m.IsRemoveMethod))
            {
                string mods = Access(m.IsPublic, m.IsFamily, m.IsAssembly) + (m.IsStatic ? " static" : "") +
                              (m.IsAbstract ? " abstract" : m.IsVirtual ? " virtual" : "");
                string ps = string.Join(", ", m.Parameters.Select(p => $"{Ty(p.ParameterType)} {p.Name}"));
                Console.WriteLine($"   method   {mods} {Ty(m.Signature?.ReturnType)} {m.Name}({ps})");
            }
        }
        return 0;
    }

    private static string Access(bool isPublic, bool isFamily, bool isAssembly) =>
        isPublic ? "public" : isFamily ? "protected" : isAssembly ? "internal" : "private";

    private static string Ty(TypeSignature? s) => s?.FullName switch
    {
        null => "?",
        "System.Void" => "void", "System.Boolean" => "bool", "System.Int32" => "int", "System.Single" => "float",
        "System.Double" => "double", "System.String" => "string", "System.Int64" => "long", "System.UInt32" => "uint",
        "System.Byte" => "byte", "System.Object" => "object",
        var other => other,
    };

    // <exe name>_Data folder that contains il2cpp_data (Aska_Data for Aska.exe).
    private static string FindDataDir(string gameDir) =>
        Directory.EnumerateDirectories(gameDir, "*_Data")
            .FirstOrDefault(d => Directory.Exists(Path.Combine(d, "il2cpp_data")))
        ?? throw new DirectoryNotFoundException($"no *_Data/il2cpp_data folder in {gameDir}");

    private static void Info(string message) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");

    private sealed record Options(string GameDir, string OutDir, string? UnityLibsDir, string? UnityLibsCache, string? DummyDir,
                                  bool ScanMethodRefs, bool Window)
    {
        public static Options Parse(string[] args)
        {
            string? game = null, output = null, unityLibs = null, libsCache = null, dummy = null;
            bool scan = true, window = false;
            for (int i = 0; i < args.Length; i++)
            {
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
                switch (args[i])
                {
                    case "--game": game = Next(); break;
                    case "--out": output = Next(); break;
                    case "--unity-libs": unityLibs = Next(); break;
                    case "--unity-libs-cache": libsCache = Next(); break;
                    case "--window": window = true; break;
                    case "--dummy": dummy = Next(); break;
                    case "--no-method-refs": scan = false; break;
                    default: throw new ArgumentException($"unknown argument {args[i]}");
                }
            }
            if (game == null || output == null) throw new ArgumentException("--game and --out are required");
            if (unityLibs != null && !Directory.Exists(unityLibs)) throw new ArgumentException($"--unity-libs {unityLibs} does not exist");
            return new Options(Path.GetFullPath(game), Path.GetFullPath(output),
                               unityLibs == null ? null : Path.GetFullPath(unityLibs),
                               libsCache == null ? null : Path.GetFullPath(libsCache),
                               dummy == null ? null : Path.GetFullPath(dummy), scan, window);
        }
    }

    private sealed class UnityLibsMissingException(string message) : Exception(message);

    private sealed class ConsoleLogger : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) => NoScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                                Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            string message = formatter(state, exception);
            if (message.EndsWith("...", StringComparison.Ordinal)) Progress.Begin(message);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Il2CppInterop {logLevel}] {message}");
            if (exception != null) Console.WriteLine(exception);
        }

        private sealed class NoScope : IDisposable
        {
            public static readonly NoScope Instance = new();
            public void Dispose() { }
        }
    }
}
