using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mjolnir.Core.Mods;

/// <summary>
/// The mods the player turned off in the Mods window, Mjolnir/Config/mod-list.json: <c>{ "version": 1, "turnedOff":
/// [ids] }</c>. Only turned-off ids are listed, so a new mod starts on. Core reads the file before loading mods; the SDK
/// writes it when the player turns a mod on or off, which takes effect at the next start (mods cannot be unloaded).
/// Ids are matched ignoring case; ids of mods that are not installed are kept, so a reinstalled mod stays off.
/// </summary>
internal static class ModList
{
    public const string FileName = "mod-list.json";
    private const int Version = 1;

    private static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private sealed record Contents(int Version, string[]? TurnedOff);

    /// <summary>The turned-off ids; none when the file is missing, and none (logged) when it cannot be read. Never throws.</summary>
    public static HashSet<string> Read(string path)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(path)) return ids;
            var contents = JsonSerializer.Deserialize<Contents>(File.ReadAllText(path), s_json);
            foreach (string? id in contents?.TurnedOff ?? [])
            {
                if (!string.IsNullOrWhiteSpace(id)) ids.Add(id.Trim());
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            Log.Warn("Mods", $"Ignoring unreadable {path}, every mod is on: {e.Message}");
            ids.Clear();
        }
        return ids;
    }

    /// <summary>Writes the turned-off ids (sorted, through a temporary file); false, logged, if it could not.</summary>
    public static bool Write(string path, IEnumerable<string> turnedOff)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var ids = turnedOff.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray();
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new Contents(Version, ids), s_json));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            Log.Warn("Mods", $"Could not write {path}: {e.Message}");
            return false;
        }
    }
}
