using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mjolnir.Core;

/// <summary>
/// Identity of the installed game build: SHA-256 of GameAssembly.dll and global-metadata.dat.
/// It tells whether the facts recorded from the analysis of the game's binaries still apply,
/// and it is the key for caches derived from the game binaries, such as interop assemblies.
/// Hashes are cached in Mjolnir\cache by file size and last write time, so they are recomputed
/// only after the game changes.
/// </summary>
internal sealed record GameIdentity(string GameAssemblySha256, string MetadataSha256)
{
    // The build analysed on 2026-10-18: game 1.48, Steam buildid 25834809, Unity 6000.3.12f1.
    public const string AnalysedGameAssemblySha256 = "cfeaec507104dbb6e126fb41446b8961531451bcd97ee513f21b6fed93716576";
    public const string AnalysedMetadataSha256 = "b55329cf7d5fea760579e8520a7cca316213b0706036d222d36cd3cf314f09ea";

    public bool IsAnalysedBuild =>
        GameAssemblySha256 == AnalysedGameAssemblySha256 && MetadataSha256 == AnalysedMetadataSha256;

    /// <summary>Short stable key for per-build caches (first 16 hex digits of a hash over both files).</summary>
    public string CacheKey =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(GameAssemblySha256 + MetadataSha256)))[..16];

    public static GameIdentity Compute(LoaderPaths paths)
    {
        string cacheFile = Path.Combine(paths.CacheDir, "game-identity.json");
        var cache = ReadCache(cacheFile);
        bool changed = false;

        string HashOf(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists) throw new FileNotFoundException("game file not found", path);
            string key = Path.GetFileName(path);
            if (cache.TryGetValue(key, out var entry) &&
                entry.Size == info.Length && entry.LastWriteUtcTicks == info.LastWriteTimeUtc.Ticks)
                return entry.Sha256;

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                                              1 << 20, FileOptions.SequentialScan);
            string sha = Convert.ToHexStringLower(SHA256.HashData(stream));
            cache[key] = new CachedHash(info.Length, info.LastWriteTimeUtc.Ticks, sha);
            changed = true;
            return sha;
        }

        var identity = new GameIdentity(HashOf(paths.GameAssemblyPath), HashOf(paths.MetadataPath));
        if (changed) WriteCache(cacheFile, cache);
        return identity;
    }

    private sealed record CachedHash(long Size, long LastWriteUtcTicks, string Sha256);

    private static Dictionary<string, CachedHash> ReadCache(string file)
    {
        try
        {
            if (File.Exists(file))
                return JsonSerializer.Deserialize<Dictionary<string, CachedHash>>(File.ReadAllText(file)) ?? [];
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Warn("Core", $"Ignoring unreadable {file}: {e.Message}");
        }
        return [];
    }

    private static void WriteCache(string file, Dictionary<string, CachedHash> cache)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            string temp = file + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(cache.OrderBy(p => p.Key).ToDictionary(),
                                                             new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Core", $"Could not write {file}: {e.Message}");
        }
    }
}
