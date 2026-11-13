using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using Mjolnir.SDK.Threading;

namespace Mjolnir.SDK.UI;

/// <summary>
/// The path of a view model binding, <c>x => x.Player.Name</c>: properties and fields from the model to the bound member.
/// Accessors are compiled once per member and shared (item templates run the same expression for every item).
/// </summary>
internal sealed class MemberPath
{
    private static readonly ConcurrentDictionary<MemberInfo, Func<object, object?>> s_getters = new();
    private static readonly ConcurrentDictionary<MemberInfo, Action<object, object?>> s_setters = new();

    private MemberPath(MemberInfo[] members) => Members = members;

    /// <summary>From the model's member to the bound one.</summary>
    public IReadOnlyList<MemberInfo> Members { get; }

    /// <exception cref="ArgumentException">Not a chain of properties and fields of the parameter, or not writable when it must be.</exception>
    public static MemberPath Parse(LambdaExpression path, bool writable)
    {
        var members = new List<MemberInfo>();
        var node = Unwrap(path.Body);
        while (node is MemberExpression access)
        {
            if (access.Member is not (PropertyInfo or FieldInfo))
                throw new ArgumentException($"binding path {path}: only properties and fields can be followed");
            members.Add(access.Member);
            node = access.Expression == null ? null : Unwrap(access.Expression);
        }
        if (node != path.Parameters[0] || members.Count == 0)
            throw new ArgumentException($"binding path {path}: write it as members of the lambda's parameter (x => x.Name, x => x.Player.Name)");
        members.Reverse();
        var last = members[^1];
        if (writable && !CanWrite(last))
            throw new ArgumentException($"binding path {path}: {last.Name} cannot be written, so it cannot be bound two-way");
        return new MemberPath([.. members]);
    }

    public static Func<object, object?> Getter(MemberInfo member) => s_getters.GetOrAdd(member, static m =>
    {
        var target = Expression.Parameter(typeof(object), "target");
        var read = Expression.MakeMemberAccess(Expression.Convert(target, m.DeclaringType!), m);
        return Expression.Lambda<Func<object, object?>>(Expression.Convert(read, typeof(object)), target).Compile();
    });

    public static Action<object, object?> Setter(MemberInfo member) => s_setters.GetOrAdd(member, static m =>
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(object), "value");
        var type = m is PropertyInfo p ? p.PropertyType : ((FieldInfo)m).FieldType;
        var write = Expression.Assign(Expression.MakeMemberAccess(Expression.Convert(target, m.DeclaringType!), m), Expression.Convert(value, type));
        return Expression.Lambda<Action<object, object?>>(write, target, value).Compile();
    });

    private static bool CanWrite(MemberInfo member) => member switch
    {
        PropertyInfo p => p.SetMethod is { IsPublic: true },
        FieldInfo f => !f.IsInitOnly && !f.IsLiteral,
        _ => false,
    };

    private static Expression Unwrap(Expression node)
    {
        while (node is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert) node = convert.Operand;
        return node;
    }
}

/// <summary>
/// A view model member: each object along the path that implements <see cref="INotifyPropertyChanged"/> is watched for
/// its next member, and a change in the middle of the path moves the watch to the new objects. The model's getters,
/// setters and events are mod code: a throw blames the model's mod. The watch is the main thread's alone (a change in the
/// middle of the path from another thread moves it on the next frame), so no lock is held while mod code runs.
/// </summary>
internal sealed class PathBinding<T, TValue>(MjolnirObject owner, MjolnirProperty<T> property, object model, MemberPath path, BindingMode mode,
                                             BindingConverter<TValue, T> convert, BindingConverter<T, TValue>? convertBack)
    : PropertyBinding<T>(owner, property, mode)
{
    private readonly object?[] _watched = new object?[path.Members.Count];
    private readonly PropertyChangedEventHandler?[] _handlers = new PropertyChangedEventHandler?[path.Members.Count];
    private readonly string _what = $"{owner} {property.Name} path";
    private int _writing;

    public override bool CanRead => true;

    // Subscribe and Unsubscribe run on the main thread (PropertyBinding.Sync).
    protected override void Subscribe()
    {
        Watch(0);
        if (TryRead(out var value)) Push(value);
    }

    protected override void Unsubscribe()
    {
        for (int i = 0; i < _watched.Length; i++) Unwatch(i);
    }

    public override bool TryRead(out T value)
    {
        value = default!;
        if (!TryWalk(path.Members.Count, out var leaf)) return false;
        return convert.TryConvert(leaf is TValue typed ? typed : default!, out value);
    }

    public override T WriteBack(T value)
    {
        if (convertBack != null && convertBack.TryConvert(value, out var back) && TryWalk(path.Members.Count - 1, out var target) && target != null)
        {
            var member = path.Members[^1];
            var setter = MemberPath.Setter(member);
            Interlocked.Increment(ref _writing);
            try
            {
                ModCall.Run(member.DeclaringType?.Assembly, _what, () => setter(target, back));
            }
            finally
            {
                Interlocked.Decrement(ref _writing);
            }
        }
        return TryRead(out var now) ? now : value;
    }

    // The object after `count` steps from the model (count 0: the model); null along the way gives null.
    private bool TryWalk(int count, out object? result)
    {
        result = model;
        for (int i = 0; i < count; i++)
        {
            if (result == null) return true;
            var member = path.Members[i];
            var getter = MemberPath.Getter(member);
            var target = result;
            if (!ModCall.TryRun(member.DeclaringType?.Assembly, _what, () => getter(target), out result)) return false;
        }
        return true;
    }

    // Watches level `from` and every level after it (main thread): level i is the object whose member i is read.
    private void Watch(int from)
    {
        for (int i = from; i < _watched.Length; i++)
        {
            Unwatch(i);
            object? target = model;
            if (i > 0 && (_watched[i - 1] is not { } parent || !TryWalkOne(parent, i - 1, out target))) target = null;
            _watched[i] = target;
            if (target is INotifyPropertyChanged notifying)
            {
                int level = i;
                string name = path.Members[i].Name;
                PropertyChangedEventHandler handler = (_, e) =>
                {
                    if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == name) OnModelChanged(level);
                };
                _handlers[i] = handler;
                ModCall.Run(target.GetType().Assembly, _what, () => notifying.PropertyChanged += handler);
            }
        }
    }

    private bool TryWalkOne(object parent, int index, out object? result)
    {
        var member = path.Members[index];
        var getter = MemberPath.Getter(member);
        return ModCall.TryRun(member.DeclaringType?.Assembly, _what, () => getter(parent), out result);
    }

    private void Unwatch(int i)
    {
        if (_watched[i] is INotifyPropertyChanged notifying && _handlers[i] is { } handler)
            ModCall.Run(notifying.GetType().Assembly, _what, () => notifying.PropertyChanged -= handler);
        _watched[i] = null;
        _handlers[i] = null;
    }

    private void OnModelChanged(int level)
    {
        if (!IsActive) return;
        bool leaf = level == _watched.Length - 1;
        if (leaf && Volatile.Read(ref _writing) > 0) return; // our own write: WriteBack reads it back
        if (leaf)
        {
            if (TryRead(out var value)) Push(value);
            return;
        }
        Dispatcher.Run(() =>
        {
            if (!IsActive) return; // unsubscribed meanwhile
            Watch(level + 1);
            if (TryRead(out var now)) Push(now);
        });
    }
}
