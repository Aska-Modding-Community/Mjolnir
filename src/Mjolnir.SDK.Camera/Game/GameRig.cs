using Cinemachine;
using UnityEngine;

namespace Mjolnir.SDK.Camera;

internal sealed partial class GameCameraSource
{
    /// <summary>One of the game's state cameras.</summary>
    private sealed class GameRig(CinemachineVirtualCamera camera, Cinemachine3rdPersonFollow? follow, CameraMode mode) : ICameraRig
    {
        public CinemachineVirtualCamera Camera { get; } = camera;

        public object Key { get; } = camera.Pointer;

        public CameraMode Mode { get; } = mode;

        public float FieldOfView
        {
            get => Camera.m_Lens.FieldOfView;
            set
            {
                var lens = Camera.m_Lens;
                lens.FieldOfView = value;
                Camera.m_Lens = lens;
            }
        }

        public float Distance
        {
            get => follow != null ? follow.CameraDistance : 0f;
            set
            {
                if (follow != null) follow.CameraDistance = value;
            }
        }

        public Vector3 ShoulderOffset
        {
            get => follow != null ? follow.ShoulderOffset : default;
            set
            {
                if (follow != null) follow.ShoulderOffset = value;
            }
        }
    }
}
