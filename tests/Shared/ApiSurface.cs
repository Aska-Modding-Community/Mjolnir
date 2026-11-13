using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Mjolnir.SDK;

namespace Mjolnir.Tests.Shared;

/// <summary>
/// The public surface of a platform assembly (Mjolnir.SDK and each of its modules), for the architecture tests: what a
/// mod can see must use only .NET, Unity and TextMeshPro types, never Core's or the game's, so a rename in the game is
/// fixed in the SDK and a Core change never reaches a mod. Linked into every SDK test project.
/// </summary>
internal static class ApiSurface
{
    /// <summary>Every public or protected signature of <paramref name="assembly"/> that uses a type <paramref name="forbidden"/> rejects.</summary>
    public static List<string> Leaks(Assembly assembly, Func<Type, bool> forbidden)
    {
        bool Uses(Type type) =>
            forbidden(type) || (type.IsGenericType && type.GetGenericArguments().Any(Uses)) ||
            (type.HasElementType && Uses(type.GetElementType()!));

        var leaks = new List<string>();
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var type in assembly.GetExportedTypes())
        {
            if (type.BaseType != null && Uses(type.BaseType)) leaks.Add($"{type} : {type.BaseType}");
            foreach (var member in type.GetMembers(all))
            {
                if (!IsVisible(member)) continue;
                foreach (var used in SignatureTypes(member).Where(Uses))
                    leaks.Add($"{type.Name}.{member.Name} uses {used}");
            }
        }
        return leaks;
    }

    /// <summary>
    /// Every public event of <paramref name="assembly"/> that breaks the SDK's event convention: a
    /// name without the On prefix (that is for methods raising or handling an event: <c>Mod.OnLoad</c>), a handler of
    /// type <see cref="EventHandler"/> or <see cref="EventHandler{TEventArgs}"/>, with a null sender for a static event; a
    /// value's change (<c>Changed</c>, or a property's name and <c>Changed</c>) carrying <see cref="ValueChangedEventArgs{T}"/>
    /// or a type derived from it, and an event before something happens (<c>…ing</c>) carrying <see cref="CancelEventArgs"/>.
    /// </summary>
    public static List<string> EventConventionBreaks(Assembly assembly) => EventConventionBreaks(assembly.GetExportedTypes());

    /// <summary>
    /// Every method of <paramref name="assembly"/> that asks <see cref="Assembly.GetCallingAssembly"/> without handing the
    /// answer straight to <c>CallingMod.Of</c>: the stack alone loses the mod in hot code (an inlined mod function, a tail
    /// call), so "which mod called me" always goes through the helper.
    /// </summary>
    public static List<string> RawCallingAssemblyUses(Assembly assembly)
    {
        var getCalling = typeof(Assembly).GetMethod(nameof(Assembly.GetCallingAssembly))!;
        var uses = new List<string>();
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var type in assembly.GetTypes())
        {
            var methods = type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all));
            foreach (var method in methods)
            {
                byte[]? il;
                try
                {
                    il = method.GetMethodBody()?.GetILAsByteArray();
                }
                catch (Exception)
                {
                    continue;
                }
                if (il == null) continue;
                for (int i = 0; i + 5 <= il.Length; i++)
                {
                    if (il[i] != 0x28 || CalledAt(method, il, i) != getCalling) continue; // call GetCallingAssembly
                    var next = i + 10 <= il.Length && il[i + 5] == 0x28 ? CalledAt(method, il, i + 5) : null;
                    if (next is not { Name: "Of", DeclaringType.Name: "CallingMod" })
                        uses.Add($"{type.FullName}.{method.Name}");
                }
            }
        }
        return uses;
    }

    private static MethodBase? CalledAt(MethodBase method, byte[] il, int at)
    {
        try
        {
            return method.Module.ResolveMethod(BitConverter.ToInt32(il, at + 1),
                                               method.DeclaringType?.GetGenericArguments(),
                                               method.IsGenericMethod ? method.GetGenericArguments() : null);
        }
        catch (Exception)
        {
            return null; // not a call: an operand's byte that happens to look like one
        }
    }

    /// <summary>The same for some types (the convention's own tests).</summary>
    public static List<string> EventConventionBreaks(IEnumerable<Type> types)
    {
        var breaks = new List<string>();
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var type in types)
        {
            foreach (var e in type.GetEvents(all).Where(IsVisible))
            {
                if (e.Name.Length > 2 && e.Name.StartsWith("On", StringComparison.Ordinal) && char.IsUpper(e.Name[2]))
                    breaks.Add($"{type.Name}.{e.Name}: no On prefix on events");
                var handler = e.EventHandlerType;
                bool standard = handler == typeof(EventHandler) ||
                                (handler is { IsGenericType: true } && handler.GetGenericTypeDefinition() == typeof(EventHandler<>));
                if (!standard) breaks.Add($"{type.Name}.{e.Name}: {handler} instead of EventHandler or EventHandler<TArgs>");
                if (IsValueChange(type, e.Name) && !Carries(handler, typeof(ValueChangedEventArgs<>)))
                    breaks.Add($"{type.Name}.{e.Name}: a value's change carries ValueChangedEventArgs<T>, not {Args(handler)}");
                if (e.Name.EndsWith("ing", StringComparison.Ordinal) && !Carries(handler, typeof(CancelEventArgs)))
                    breaks.Add($"{type.Name}.{e.Name}: an event before something happens carries CancelEventArgs, not {Args(handler)}");
            }
        }
        return breaks;
    }

    // Changed (a bindable's value), or a property's name and Changed (ValueChanged, IsCheckedChanged); not SelectionChanged
    // or PropertyChanged, which no property names.
    private static bool IsValueChange(Type type, string name)
    {
        if (name == "Changed") return true;
        if (!name.EndsWith("Changed", StringComparison.Ordinal)) return false;
        string property = name[..^"Changed".Length];
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Any(p => p.Name == property);
    }

    // Whether the handler's arguments are the type or derive from it (a generic definition matches any of its types).
    private static bool Carries(Type? handler, Type args)
    {
        for (var type = Args(handler); type != null; type = type.BaseType)
        {
            if (type == args || (type.IsGenericType && type.GetGenericTypeDefinition() == args)) return true;
        }
        return false;
    }

    private static Type? Args(Type? handler) =>
        handler is { IsGenericType: true } && handler.GetGenericTypeDefinition() == typeof(EventHandler<>) ? handler.GetGenericArguments()[0]
        : handler == typeof(EventHandler) ? typeof(EventArgs) : null;

    /// <summary>A type of Mjolnir.Core: mods talk to the SDK only.</summary>
    public static bool IsCore(Type type) => type.Assembly.GetName().Name == "Mjolnir.Core";

    /// <summary>A type of the game's own assemblies (not Unity's or TextMeshPro's).</summary>
    public static bool IsGame(Type type) => IsGameAssembly(type.Assembly.GetName().Name);

    public static bool IsGameAssembly(string? name) =>
        name is "Assembly-CSharp" or "Assembly-CSharp-firstpass" or "SandSailorStudio" ||
        name?.StartsWith("Fusion.", StringComparison.Ordinal) == true;

    /// <summary>The assemblies allowed to see <paramref name="assembly"/>'s internals.</summary>
    public static IEnumerable<string> FriendsOf(Assembly assembly) =>
        assembly.GetCustomAttributes<System.Runtime.CompilerServices.InternalsVisibleToAttribute>().Select(a => a.AssemblyName);

    // Public, or protected and so visible to mods deriving from a platform type.
    private static bool IsVisible(MemberInfo member) => member switch
    {
        MethodBase m => m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly,
        PropertyInfo p => p.GetAccessors(true).Any(a => a.IsPublic || a.IsFamily || a.IsFamilyOrAssembly),
        FieldInfo f => f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly,
        EventInfo e => e.AddMethod is { } add && (add.IsPublic || add.IsFamily),
        _ => false,
    };

    private static IEnumerable<Type> SignatureTypes(MemberInfo member) => member switch
    {
        MethodInfo m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType),
        ConstructorInfo c => c.GetParameters().Select(p => p.ParameterType),
        PropertyInfo p => [p.PropertyType],
        FieldInfo f => [f.FieldType],
        EventInfo e => e.EventHandlerType is { } t ? [t] : [],
        _ => [],
    };
}
