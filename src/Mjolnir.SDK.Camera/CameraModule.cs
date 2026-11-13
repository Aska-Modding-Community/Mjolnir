using Mjolnir.SDK;
using Mjolnir.SDK.Camera;

[assembly: SdkModule(typeof(CameraModule))]

namespace Mjolnir.SDK.Camera;

/// <summary>
/// The camera module's entry: Mjolnir.SDK starts it when the first mod attaches (phase A). It connects
/// <see cref="GameCamera"/> to the game's camera and wraps the game's camera update each frame with a hidden patch.
/// </summary>
internal sealed class CameraModule : ISdkModule
{
    public void Start() => GameCameraSource.Start();
}
