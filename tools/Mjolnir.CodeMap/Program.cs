using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Iced.Intel;
using Decoder = Iced.Intel.Decoder;

namespace Mjolnir.CodeMap;

/// <summary>
/// Static code map of GameAssembly.dll by method name: the research tool behind the extension-point map
/// (step 7). It answers in seconds who calls a method, what it calls and what
/// it does, without a full analysis of the binary; a decompiler stays for the few functions whose logic is unclear.
/// Method RVAs and field offsets come from the Cpp2IL dummy assemblies: tools/gen-interop.ps1 -DummyDir build/interop-dummy.
/// Only direct calls are named; virtual (vtable) and interface calls show as "vcall [reg+offset]" / "indirect".
/// </summary>
internal static class Program
{
    private const string Usage = """
        usage: Mjolnir.CodeMap <command> <regex> [--game <dir>] [--dummy <dir>] [--max <n>]
          find     methods whose "Type::Method(params)" matches: RVA, size, flags
          callees  what the matching methods call: named direct calls, vtable calls, indirect calls
          callers  which methods call the matching methods directly (no direct callers: inlined, or only reached
                   virtually, through a delegate or from the engine)
          disasm   annotated disassembly: call targets named, [reg+offset] matched to the method's own type fields
          fields   instance field offsets of the matching types, base types included (for [reg+offset] the
                   disassembly could not attribute, e.g. a field of another object)
        regex is case-insensitive, e.g. "CharacterMovement::TryDrainStamina" or "^SSSGame\.UI\.PlayerHUDMenu::"
        defaults: --game MJOLNIR_GAME_DIR or the Steam library that has ASKA, --dummy <repo>\build\interop-dummy, --max 20
        """;

    private static int Main(string[] args)
    {
        try
        {
            var options = Options.Parse(args);
            var watch = Stopwatch.StartNew();
            var image = new GameImage(Path.Combine(options.GameDir, "GameAssembly.dll"));
            var index = CodeIndex.Load(options.DummyDir).WithBounds(image.FunctionEnds);
            Console.Error.WriteLine($"[{index.Methods.Count} methods, {index.Types.Count} types, {image.Exports.Count} exports, loaded in {watch.ElapsedMilliseconds} ms]");
            var map = new Map(index, image);
            return options.Command switch
            {
                "find" => map.Find(options.Pattern, options.Max),
                "callees" => map.Callees(options.Pattern, options.Max),
                "callers" => map.Callers(options.Pattern, options.Max),
                "disasm" => map.Disasm(options.Pattern, options.Max),
                "fields" => map.Fields(options.Pattern, options.Max),
                _ => throw new ArgumentException($"unknown command '{options.Command}'"),
            };
        }
        catch (Exception e) when (e is ArgumentException or FormatException or OverflowException)
        {
            Console.Error.WriteLine(e.Message);
            Console.Error.WriteLine(Usage);
            return 2;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or BadImageFormatException)
        {
            // A game or dummy folder that is not there, a file in use, an image that cannot be read.
            Console.Error.WriteLine($"error: {e.Message}");
            return 1;
        }
    }

    private sealed record Options(string Command, Regex Pattern, string GameDir, string DummyDir, int Max)
    {
        public static Options Parse(string[] args)
        {
            if (args.Length < 2) throw new ArgumentException("missing command or regex");
            string? game = null;
            string? dummy = null;
            int max = 20;
            for (int i = 2; i < args.Length; i++)
            {
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
                switch (args[i])
                {
                    case "--game": game = Next(); break;
                    case "--dummy": dummy = Next(); break;
                    case "--max": max = int.Parse(Next()); break;
                    default: throw new ArgumentException($"unknown option {args[i]}");
                }
            }
            dummy ??= Path.Combine(RepoRoot(), "build", "interop-dummy");
            if (!Directory.Exists(dummy))
                throw new ArgumentException($"no dummy assemblies in {dummy}: run tools/gen-interop.ps1 -DummyDir build/interop-dummy");
            return new Options(args[0], new Regex(args[1], RegexOptions.IgnoreCase), game ?? GameLocator.Find(), dummy, max);
        }

        private static string RepoRoot()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Mjolnir.slnx"))) return dir.FullName;
            }
            return Directory.GetCurrentDirectory();
        }
    }
}

/// <summary>The commands, over one loaded index and image.</summary>
internal sealed class Map(CodeIndex index, GameImage image)
{
    public int Find(Regex pattern, int max)
    {
        var found = Matching(pattern).ToList();
        foreach (var m in found.Take(max * 5))
        {
            int folded = index.ByRva[m.Rva].Count - 1;
            Console.WriteLine($"0x{m.Rva:X8}  {m.Length,6}  {Flags(m),-15} {m.Name}" + (folded > 0 ? $"   [code shared with {folded} other method(s)]" : ""));
        }
        Console.WriteLine($"{found.Count} method(s)");
        return found.Count > 0 ? 0 : 1;
    }

    public int Callees(Regex pattern, int max)
    {
        var found = Matching(pattern).ToList();
        foreach (var m in found.Take(max))
        {
            Console.WriteLine($"== {m.Name}  0x{m.Rva:X} ({m.Length} bytes) {Flags(m)}");
            var calls = new List<(string Kind, string Target)>();
            foreach (var ins in Decode(m))
            {
                var call = Classify(m, ins);
                if (call != null) calls.Add(call.Value);
            }
            foreach (var group in calls.GroupBy(c => c))
                Console.WriteLine($"   {group.Key.Kind,-8} x{group.Count(),-3} {group.Key.Target}");
            if (calls.Count == 0) Console.WriteLine("   (no calls)");
        }
        Console.WriteLine($"{found.Count} method(s)" + (found.Count > max ? $", first {max} shown" : ""));
        return found.Count > 0 ? 0 : 1;
    }

    public int Callers(Regex pattern, int max)
    {
        var targets = Matching(pattern).Select(m => m.Rva).ToHashSet();
        if (targets.Count == 0)
        {
            Console.WriteLine("no matching method");
            return 1;
        }

        var callers = targets.ToDictionary(t => t, _ => new Dictionary<MethodEntry, int>());
        foreach (var m in index.Methods.DistinctBy(m => m.Rva))
        {
            foreach (var ins in Decode(m))
            {
                if (ins.Op0Kind != OpKind.NearBranch64 || ins.FlowControl is not (FlowControl.Call or FlowControl.UnconditionalBranch)) continue;
                ulong target = ins.NearBranchTarget;
                if (m.Contains(target) || !callers.TryGetValue(target, out var from)) continue;
                from[m] = from.GetValueOrDefault(m) + 1;
            }
        }

        foreach (ulong target in targets.OrderBy(t => index.ByRva[t][0].Name))
        {
            var names = index.ByRva[target];
            Console.WriteLine($"== {names[0].Name}  0x{target:X}" + (names.Count > 1 ? $"   [code shared with {names.Count - 1} other method(s): callers may mean any of them]" : ""));
            var from = callers[target];
            if (from.Count == 0) Console.WriteLine("   no direct callers (inlined everywhere, or reached only virtually, through a delegate or from the engine)");
            foreach (var (caller, count) in from.OrderBy(c => c.Key.Name).Take(max))
                Console.WriteLine($"   x{count,-3} {caller.Name}");
            if (from.Count > max) Console.WriteLine($"   … {from.Count - max} more");
        }
        return 0;
    }

    public int Disasm(Regex pattern, int max)
    {
        var found = Matching(pattern).ToList();
        var formatter = new IntelFormatter(new Symbols(this)) { Options = { FirstOperandCharIndex = 8, HexPrefix = "0x", HexSuffix = "" } };
        var output = new StringOutput();
        var info = new InstructionInfoFactory();
        foreach (var m in found.Take(Math.Min(max, 5)))
        {
            Console.WriteLine($"== {m.Name}  0x{m.Rva:X} ({m.Length} bytes) {Flags(m)}");
            // Registers known to hold `this` (rcx on entry); only [this+offset] gets a field name.
            var self = m.IsStatic ? new HashSet<Register>() : [Register.RCX];
            foreach (var ins in Decode(m))
            {
                formatter.Format(ins, output);
                Console.WriteLine($"  {ins.IP:X8}  {output.ToStringAndReset(),-60}{FieldComment(m, ins, self)}");
                TrackThis(ins, self, info);
            }
        }
        Console.WriteLine($"{found.Count} method(s)" + (found.Count > 5 ? ", first 5 shown" : ""));
        return found.Count > 0 ? 0 : 1;
    }

    public int Fields(Regex pattern, int max)
    {
        var found = index.Types.Values.Where(t => pattern.IsMatch(t.Name)).OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
        foreach (var type in found.Take(max))
        {
            Console.WriteLine($"== {type.Name}" + (type.BaseType != null ? $" : {type.BaseType}" : ""));
            var fields = new SortedDictionary<int, string>();
            for (string? name = type.Name; name != null && index.Types.TryGetValue(name, out var t); name = t.BaseType)
            {
                foreach (var (offset, field) in t.Fields)
                    fields.TryAdd(offset, t == type ? field : $"{field}   ({t.Name})");
            }
            foreach (var (offset, field) in fields) Console.WriteLine($"   0x{offset:X4}  {field}");
        }
        Console.WriteLine($"{found.Count} type(s)" + (found.Count > max ? $", first {max} shown" : ""));
        return found.Count > 0 ? 0 : 1;
    }

    /// <summary>A name for a code address: a game method, an il2cpp_* export, or the section it lies in.</summary>
    public string? NameOf(ulong rva, bool fallback)
    {
        // Identical code folding: one body may stand for several methods, and any of them may be the real callee.
        if (index.ByRva.TryGetValue(rva, out var methods))
            return methods.Count == 1 ? methods[0].Name
                 : methods.Count <= 3 ? "{" + string.Join(" | ", methods.Select(m => m.Name)) + "}"
                 : $"{{{methods[0].Name} | … {methods.Count - 1} more folded}}";
        if (image.Exports.TryGetValue(rva, out var export)) return export;
        if (!fallback) return null;
        string? section = image.SectionOf(rva);
        return section == ".text" ? $"runtime_{rva:X} (libil2cpp)" : $"sub_{rva:X} ({section ?? "?"})";
    }

    private IEnumerable<MethodEntry> Matching(Regex pattern) =>
        index.Methods.Where(m => pattern.IsMatch(m.Name)).OrderBy(m => m.Name, StringComparer.Ordinal);

    private (string Kind, string Target)? Classify(MethodEntry m, Instruction ins)
    {
        if (ins.Op0Kind == OpKind.NearBranch64 && ins.FlowControl is FlowControl.Call or FlowControl.UnconditionalBranch)
        {
            ulong target = ins.NearBranchTarget;
            if (m.Contains(target)) return null;
            return (ins.FlowControl == FlowControl.Call ? "call" : "tailjmp", NameOf(target, fallback: true)!);
        }
        if (ins.FlowControl is not (FlowControl.IndirectCall or FlowControl.IndirectBranch)) return null;
        string kind = ins.FlowControl == FlowControl.IndirectCall ? "vcall" : "vjmp";
        if (ins.Op0Kind != OpKind.Memory) return (kind == "vcall" ? "indirect" : "jmpreg", ins.Op0Register.ToString().ToLowerInvariant());
        if (ins.IsIPRelativeMemoryOperand) return (kind, $"[rip global 0x{ins.IPRelativeMemoryAddress:X}]");
        return (kind, $"[{ins.MemoryBase.ToString().ToLowerInvariant()}+0x{ins.MemoryDisplacement64:X}]");
    }

    private string FieldComment(MethodEntry m, Instruction ins, HashSet<Register> self)
    {
        for (int i = 0; i < ins.OpCount; i++)
        {
            if (ins.GetOpKind(i) != OpKind.Memory || ins.IsIPRelativeMemoryOperand) continue;
            if (!self.Contains(ins.MemoryBase.GetFullRegister()) || ins.MemoryIndex != Register.None) continue;
            int offset = (int)ins.MemoryDisplacement64;
            var fields = index.FieldsAt(m.Type, offset).Select(f => $"{Short(f.Type)}.{f.Field}").ToList();
            if (fields.Count > 0) return $"; this.{string.Join(" | ", fields)}";
        }
        return "";
    }

    // Follows `this` through register copies; any other write, and every call (volatile registers), drops it.
    private static void TrackThis(Instruction ins, HashSet<Register> self, InstructionInfoFactory info)
    {
        if (self.Count == 0) return;
        if (ins.Mnemonic == Mnemonic.Mov && ins.Op0Kind == OpKind.Register && ins.Op1Kind == OpKind.Register)
        {
            var to = ins.Op0Register.GetFullRegister();
            if (self.Contains(ins.Op1Register.GetFullRegister())) self.Add(to);
            else self.Remove(to);
            return;
        }
        foreach (var used in info.GetInfo(ins).GetUsedRegisters())
        {
            if (used.Access is OpAccess.Write or OpAccess.ReadWrite or OpAccess.CondWrite or OpAccess.ReadCondWrite)
                self.Remove(used.Register.GetFullRegister());
        }
        if (ins.FlowControl is FlowControl.Call or FlowControl.IndirectCall)
        {
            foreach (var r in new[] { Register.RAX, Register.RCX, Register.RDX, Register.R8, Register.R9, Register.R10, Register.R11 })
                self.Remove(r);
        }
    }

    private IEnumerable<Instruction> Decode(MethodEntry m)
    {
        if (m.Length <= 0 || m.FileOffset + m.Length > image.Bytes.Length) yield break;
        var decoder = Decoder.Create(64, new ByteArrayCodeReader(image.Bytes, (int)m.FileOffset, m.Length), m.Rva);
        ulong end = m.Rva + (ulong)m.Length;
        while (decoder.IP < end)
        {
            decoder.Decode(out var ins);
            yield return ins;
        }
    }

    private static string Flags(MethodEntry m) => (m.IsStatic ? "static " : "") + (m.IsVirtual ? "virtual" : "");

    private static string Short(string typeName) => typeName[(typeName.LastIndexOf('.') + 1)..];

    private sealed class Symbols(Map map) : ISymbolResolver
    {
        public bool TryGetSymbol(in Instruction instruction, int operand, int instructionOperand, ulong address, int addressSize, out SymbolResult symbol)
        {
            string? name = instruction.FlowControl is FlowControl.Call or FlowControl.UnconditionalBranch or FlowControl.ConditionalBranch
                ? map.NameOf(address, fallback: false)
                : null;
            symbol = name != null ? new SymbolResult(address, name) : default;
            return name != null;
        }
    }
}
