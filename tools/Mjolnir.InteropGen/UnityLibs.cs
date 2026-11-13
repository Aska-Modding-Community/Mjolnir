using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace Mjolnir.InteropGen;

/// <summary>
/// Unity's base libraries for the game's Unity version, which the generator unstrips the engine's methods with: found in a
/// cache folder, else downloaded there once (the same archive tools/gen-interop.ps1 takes). Without them the generation
/// stops (<see cref="Program.UnityLibsMissingExitCode"/>): assemblies made without unstripping would be stamped as current.
/// </summary>
internal static class UnityLibs
{
    private const string Source = "https://unity.bepinex.dev/libraries/{0}.zip";

    public static string? Find(string cacheDir, string gameDir, Action<string> info)
    {
        string? version = VersionOf(Path.Combine(gameDir, "UnityPlayer.dll"));
        if (version == null)
        {
            info("Unity libraries: the Unity version of UnityPlayer.dll is unreadable");
            return null;
        }
        string dir = Path.Combine(cacheDir, version);
        if (File.Exists(Path.Combine(dir, "UnityEngine.CoreModule.dll"))) return dir;

        Progress.Set(Stage.Download);
        string url = string.Format(Source, version);
        string zip = Path.Combine(cacheDir, version + ".zip.tmp");
        string unpacked = dir + ".tmp";
        try
        {
            var watch = Stopwatch.StartNew();
            Directory.CreateDirectory(cacheDir);
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) })
            using (var response = http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult())
            {
                response.EnsureSuccessStatusCode();
                using var file = File.Create(zip);
                response.Content.CopyToAsync(file).GetAwaiter().GetResult();
            }
            if (Directory.Exists(unpacked)) Directory.Delete(unpacked, true);
            ZipFile.ExtractToDirectory(zip, unpacked);
            if (!File.Exists(Path.Combine(unpacked, "UnityEngine.CoreModule.dll")))
                throw new InvalidDataException("the archive holds no UnityEngine.CoreModule.dll");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.Move(unpacked, dir);
            info($"Unity libraries {version} downloaded from {url} ({watch.Elapsed.TotalSeconds:F1} s)");
            return dir;
        }
        catch (Exception e)
        {
            info($"Unity libraries {version} not downloaded from {url} ({e.GetType().Name}: {e.Message})");
            return null;
        }
        finally
        {
            TryDelete(zip);
            if (Directory.Exists(unpacked)) try { Directory.Delete(unpacked, true); } catch (IOException) { }
        }
    }

    // UnityPlayer.dll's ProductVersion "6000.3.12f1 (fca03ac9b0d5)" → "6000.3.12".
    private static string? VersionOf(string unityPlayer)
    {
        if (!File.Exists(unityPlayer)) return null;
        var match = Regex.Match(FileVersionInfo.GetVersionInfo(unityPlayer).ProductVersion ?? "", @"^(\d+\.\d+\.\d+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
