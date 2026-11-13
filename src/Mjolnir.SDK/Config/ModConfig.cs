using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Tomlyn;
using Tomlyn.Model;

namespace Mjolnir.SDK.Config;

/// <summary>
/// A mod's TOML config file (Mjolnir/Config/&lt;id&gt;.toml). Bind each setting once, usually in OnLoad.
/// <list type="bullet">
/// <item>A missing file or key gets its default, written with the description as a comment.</item>
/// <item>The file is rewritten only when a bound key was missing or invalid, a value was set, or on <see cref="Save"/>;
/// otherwise hand edits and comments stay untouched. Keys the mod does not bind are kept.</item>
/// <item>A file with a TOML syntax error is never written: defaults are used until it is fixed.</item>
/// </list>
/// Supported value types: bool, int, long, float, double, string and enums (stored by name). Numbers may get a range
/// (<see cref="ConfigEntry{T}.WithRange"/>). Every bound setting also appears in the game's Mods window, where the player
/// can change it (<see cref="ConfigEntry{T}.WithDisplayName"/>, <see cref="ConfigEntry{T}.WithOrder"/> and
/// <see cref="ConfigEntry{T}.WithRestartRequired"/> shape how it shows there); <see cref="ConfigEntry{T}.Changed"/> tells the mod.
/// </summary>
public sealed partial class ModConfig
{
    private static readonly TomlSerializerOptions s_toml = new();

    private readonly object _lock = new();
    private readonly string _header;
    private readonly Action<string> _warn;
    private readonly TomlTable _file;
    private readonly bool _broken;
    private readonly List<IConfigEntry> _entries = [];
    private bool _changed;

    private ModConfig(string path, string header, Action<string> warn, TomlTable file, bool broken, bool exists)
    {
        FilePath = path;
        _header = header;
        _warn = warn;
        _file = file;
        _broken = broken;
        _changed = !exists;
    }

    public string FilePath { get; }

    /// <summary>The bound settings in binding order (the generated settings window lists them so).</summary>
    internal IReadOnlyList<IConfigEntry> Entries
    {
        get
        {
            lock (_lock) return _entries.ToArray();
        }
    }

    internal static ModConfig Load(string path, string header, Action<string> warn)
    {
        if (!File.Exists(path)) return new ModConfig(path, header, warn, new TomlTable(), broken: false, exists: false);
        string name = Path.GetFileName(path);
        try
        {
            var table = TomlSerializer.Deserialize<TomlTable>(File.ReadAllText(path), s_toml) ?? new TomlTable();
            return new ModConfig(path, header, warn, table, broken: false, exists: true);
        }
        catch (TomlException e)
        {
            warn($"{name} line {e.Line}, column {e.Column}: {FirstLine(e.Message)}. " +
                 "Using defaults; the file is not written until it is fixed");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            warn($"{name} could not be read ({e.Message}). Using defaults; the file is not written");
        }
        return new ModConfig(path, header, warn, new TomlTable(), broken: true, exists: true);
    }

    /// <summary>
    /// Declares a setting and returns it with the value from the file, or the default if the key is missing or invalid.
    /// Binding the same section and key again returns the same entry.
    /// </summary>
    public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(section);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!IsSupported(typeof(T)))
            throw new NotSupportedException($"config type {typeof(T).Name} is not supported (bool, int, long, float, double, string, enums, KeyGesture)");
        ArgumentNullException.ThrowIfNull(defaultValue);

        lock (_lock)
        {
            var existing = _entries.FirstOrDefault(e => e.Section == section && e.Key == key);
            if (existing != null)
                return existing as ConfigEntry<T> ?? throw new InvalidOperationException(
                    $"[{section}] {key} is already bound as {existing.ValueType.Name}");

            T value = defaultValue;
            if (_file.TryGetValue(section, out var s) && s is TomlTable table && table.TryGetValue(key, out var raw))
            {
                if (TryConvert(raw, typeof(T), out var converted))
                {
                    value = (T)converted;
                }
                else
                {
                    _warn($"[{section}] {key} = {raw} is not a valid {typeof(T).Name}; using the default {Display(defaultValue)}");
                    _changed = true;
                }
            }
            else
            {
                _changed = true;
            }

            var entry = new ConfigEntry<T>(this, section, key, defaultValue, value, description);
            _entries.Add(entry);
            return entry;
        }
    }

    /// <summary>
    /// Writes the file now (not if it has a syntax error). A file that cannot be written (read-only, in use, no room) is
    /// logged as a warning and written at the next save.
    /// </summary>
    public void Save()
    {
        lock (_lock)
        {
            if (_broken)
            {
                _warn($"{Path.GetFileName(FilePath)} is not saved: it has a syntax error");
                return;
            }
            string temp = FilePath + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(temp, Render(), new UTF8Encoding(false));
                File.Move(temp, FilePath, overwrite: true);
                _changed = false;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _warn($"{Path.GetFileName(FilePath)} could not be saved ({e.Message}); it is tried again at the next save");
                try { File.Delete(temp); }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    internal void SaveIfChanged()
    {
        bool save;
        lock (_lock) save = _changed && !_broken;
        if (save) Save();
    }

    internal void MarkChanged()
    {
        lock (_lock) _changed = true;
    }

    internal void Warn(string message) => _warn(message);

    private string Render()
    {
        var text = new StringBuilder();
        text.Append("# ").Append(_header).Append(" - config written by Mjolnir.\n");
        text.Append("# Edit values while the game is not running. Keys the mod does not use are kept.\n");

        // Unknown top-level keys must come before the first [section].
        var rootExtras = new TomlTable();
        foreach (var (k, v) in _file)
        {
            if (v is not TomlTable) rootExtras[k] = v;
        }
        if (rootExtras.Count > 0) text.Append('\n').Append(Serialize(rootExtras));

        var sections = _entries.Select(e => e.Section).Distinct().ToList();
        foreach (string section in sections)
        {
            text.Append('\n').Append('[').Append(TomlKey(section)).Append("]\n");
            foreach (var entry in _entries.Where(e => e.Section == section))
            {
                text.Append('\n');
                if (!string.IsNullOrWhiteSpace(entry.Description))
                {
                    foreach (string line in entry.Description.Split('\n')) text.Append("# ").Append(line.TrimEnd('\r')).Append('\n');
                }
                if (entry.IsRestartRequired) text.Append("# Takes effect after the game restarts.\n");
                text.Append("# Default: ").Append(entry.DefaultDisplay).Append('\n');
                if (entry.RangeDisplay != null) text.Append("# Range: ").Append(entry.RangeDisplay).Append('\n');
                if (entry.ValueType.IsEnum)
                    text.Append("# Allowed: ").Append(string.Join(", ", Enum.GetNames(entry.ValueType))).Append('\n');
                if (entry.ValueType == typeof(KeyGesture))
                    text.Append("# A key with Ctrl, Shift or Alt (\"Ctrl+Shift+F8\", Unity Input System key names), or \"None\".\n");
                text.Append(Serialize(new TomlTable { [entry.Key] = entry.TomlValue }));
            }

            if (_file.TryGetValue(section, out var s) && s is TomlTable existing)
            {
                var extras = new TomlTable();
                foreach (var (k, v) in existing)
                {
                    if (_entries.Any(e => e.Section == section && e.Key == k)) continue;
                    if (v is TomlTable) _warn($"[{section}.{k}] is a sub-table, which Mjolnir does not keep inside a bound section; it is dropped");
                    else extras[k] = v;
                }
                if (extras.Count > 0) text.Append('\n').Append(Serialize(extras));
            }
        }

        var otherSections = new TomlTable();
        foreach (var (k, v) in _file)
        {
            if (v is TomlTable && !sections.Contains(k)) otherSections[k] = v;
        }
        if (otherSections.Count > 0) text.Append('\n').Append(Serialize(otherSections));

        return text.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
    }

    private static string Serialize(TomlTable table)
    {
        string s = TomlSerializer.Serialize(table, s_toml);
        return s.EndsWith('\n') ? s : s + "\n";
    }

    private static string FirstLine(string message) => message.Split('\n')[0].Trim();

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex BareKey();

    private static string TomlKey(string key) =>
        BareKey().IsMatch(key) ? key : "\"" + key.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    internal static bool IsNumber(Type t) => t == typeof(int) || t == typeof(long) || t == typeof(float) || t == typeof(double);

    private static bool IsSupported(Type t) =>
        t == typeof(bool) || t == typeof(int) || t == typeof(long) || t == typeof(float) || t == typeof(double) ||
        t == typeof(string) || t.IsEnum || t == typeof(KeyGesture);

    internal static object ToToml(object value) => value switch
    {
        int i => (long)i,
        float f => double.Parse(f.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
        Enum e => e.ToString(),
        KeyGesture g => g.ToString(),
        _ => value,
    };

    internal static string Display(object? value) => value switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        string s => "\"" + s + "\"",
        KeyGesture g => "\"" + g + "\"",
        IFormattable f and not Enum => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static bool TryConvert(object raw, Type type, out object value)
    {
        value = null!;
        if (type == typeof(bool) && raw is bool b) value = b;
        else if (type == typeof(long) && raw is long l) value = l;
        else if (type == typeof(int) && raw is long li && li is >= int.MinValue and <= int.MaxValue) value = (int)li;
        else if (type == typeof(double) && raw is double or long) value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
        else if (type == typeof(float) && raw is double or long) value = Convert.ToSingle(raw, CultureInfo.InvariantCulture);
        else if (type == typeof(string) && raw is string str) value = str;
        else if (type.IsEnum && raw is string name && Enum.TryParse(type, name, ignoreCase: true, out var parsed) &&
                 Enum.IsDefined(type, parsed!)) value = parsed!;
        else if (type == typeof(KeyGesture) && raw is string keys && KeyGesture.TryParse(keys, out var gesture)) value = gesture;
        return value != null;
    }
}
