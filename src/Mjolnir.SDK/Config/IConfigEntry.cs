using System;

namespace Mjolnir.SDK.Config;

internal interface IConfigEntry
{
    string Section { get; }
    string Key { get; }
    string? Description { get; }
    string? DisplayName { get; }
    int Order { get; }
    bool IsRestartRequired { get; }
    Type ValueType { get; }
    object TomlValue { get; }
    string DefaultDisplay { get; }
    string? RangeDisplay { get; }

    /// <summary>The value boxed, for the generated settings window. Setting it goes through Value: range, Changed.</summary>
    object BoxedValue { get; set; }

    object BoxedDefault { get; }

    /// <summary>The range from WithRange as doubles, or null.</summary>
    (double Minimum, double Maximum)? Range { get; }

    /// <summary>Typed access without reflection: calls the visitor with the ConfigEntry&lt;T&gt; itself.</summary>
    TResult Accept<TResult>(IConfigEntryVisitor<TResult> visitor);
}
