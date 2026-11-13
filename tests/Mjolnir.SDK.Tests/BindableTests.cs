using System;
using System.Collections.Generic;
using Xunit;

namespace Mjolnir.SDK.Tests;

/// <summary><see cref="IBindable{T}"/> and <see cref="BindableExtensions.Map{TSource, TResult}"/>, without any UI.</summary>
public sealed class BindableTests
{
    [Fact]
    public void MapConvertsBothWays()
    {
        var source = new Source<int>(3);
        var mapped = source.Map(v => (float)v, f => (int)MathF.Round(f));
        var seen = new List<(object? Sender, float Old, float New)>();
        mapped.Changed += (s, e) => seen.Add((s, e.OldValue, e.NewValue));

        mapped.Value = 6.6f;
        Assert.Equal(7, source.Value);
        Assert.Equal([(mapped, 3f, 7f)], seen);
        Assert.Equal(7f, mapped.Value);
    }

    [Fact]
    public void AMappingListensToItsSourceOnlyWhileSomeoneListensToIt()
    {
        var source = new Source<int>(1);
        var mapped = source.Map(v => v * 2, v => v / 2);
        Assert.Equal(0, source.Listeners);

        EventHandler<ValueChangedEventArgs<int>> handler = (_, _) => { };
        mapped.Changed += handler;
        Assert.Equal(1, source.Listeners);
        mapped.Changed -= handler;
        Assert.Equal(0, source.Listeners);
    }

    [Fact]
    public void AThrowingConversionGivesTheDefaultAndWritesNothing()
    {
        var source = new Source<int>(5);
        var mapped = source.Map<int, string>(_ => throw new InvalidOperationException("to"), _ => throw new InvalidOperationException("from"));
        Assert.Null(mapped.Value);
        mapped.Value = "x";
        Assert.Equal(5, source.Value);
    }

    [Fact]
    public void AMapOneWayIsReadOnlyAndFollowsItsSource()
    {
        var source = new Source<int>(2);
        var mapped = source.Map(v => $"{v} villagers");
        Assert.IsNotAssignableFrom<IBindable<string>>(mapped); // a binding to it goes one way
        var seen = new List<string>();
        mapped.Changed += (_, e) => seen.Add(e.NewValue);
        source.Value = 3;
        Assert.Equal("3 villagers", mapped.Value);
        Assert.Equal(["3 villagers"], seen);
    }

    /// <summary>A plain bindable value, like the UI module's BindableValue.</summary>
    private sealed class Source<T>(T value) : IBindable<T>
    {
        private EventHandler<ValueChangedEventArgs<T>>? _changed;
        private T _value = value;

        public int Listeners => _changed?.GetInvocationList().Length ?? 0;

        public T Value
        {
            get => _value;
            set
            {
                var old = _value;
                if (EqualityComparer<T>.Default.Equals(old, value)) return;
                _value = value;
                _changed?.Invoke(this, new ValueChangedEventArgs<T>(old, value));
            }
        }

        public event EventHandler<ValueChangedEventArgs<T>>? Changed
        {
            add => _changed += value;
            remove => _changed -= value;
        }
    }
}
