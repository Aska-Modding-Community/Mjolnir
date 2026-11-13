using Mjolnir.SDK;
using Mjolnir.SDK.Terrain;

[assembly: SdkModule(typeof(TerrainModule))]

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// The terrain module's entry: Mjolnir.SDK starts it when the first mod attaches (phase A). It connects
/// <see cref="Landscape"/> to the game's ground, patches where the game changes it, and adds the console's commands.
/// </summary>
internal sealed class TerrainModule : ISdkModule
{
    public void Start() => GameTerrain.Start();
}
