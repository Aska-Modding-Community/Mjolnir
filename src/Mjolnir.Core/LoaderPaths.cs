using System;
using System.IO;

namespace Mjolnir.Core;

internal sealed record LoaderPaths(string GameDir, string LoaderDir)
{
    public string CoreDir => Path.Combine(LoaderDir, "core");
    public string InteropDir => Path.Combine(LoaderDir, "interop");
    public string ModsDir => Path.Combine(LoaderDir, "Mods");
    public string ConfigDir => Path.Combine(LoaderDir, "Config");
    public string CacheDir => Path.Combine(LoaderDir, "cache");

    /// <summary>Config\mod-list.json: the mods the player turned off (Mods.ModList).</summary>
    public string ModListPath => Path.Combine(ConfigDir, Mods.ModList.FileName);

    public string GameAssemblyPath => Path.Combine(GameDir, "GameAssembly.dll");
    public string UnityPlayerPath => Path.Combine(GameDir, "UnityPlayer.dll");

    /// <summary>&lt;exe name&gt;_Data\il2cpp_data\Metadata\global-metadata.dat (Aska_Data for Aska.exe).</summary>
    public string MetadataPath => Path.Combine(GameDir, DataFolderName, "il2cpp_data", "Metadata", "global-metadata.dat");

    private static string DataFolderName =>
        Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "Aska.exe") + "_Data";
}
