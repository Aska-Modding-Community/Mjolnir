using System;
using Cinemachine;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.Camera;

internal sealed partial class GameCameraSource
{
    /// <summary>The virtual camera of a mod's camera: no body or aim of Cinemachine's, so it stands where its object is.</summary>
    private sealed class OwnCamera
    {
        private readonly GameObject _holder;
        private readonly CinemachineVirtualCamera _camera;

        private OwnCamera(GameObject holder, CinemachineVirtualCamera camera)
        {
            _holder = holder;
            _camera = camera;
        }

        public bool IsAlive => _holder != null && _camera != null;

        public static OwnCamera Create(ModCamera camera, CinemachineVirtualCamera? lensOf)
        {
            // In the world's scene, so it goes with the world.
            var holder = new GameObject($"Mjolnir camera {camera.Owner.GetName().Name} {camera.Sequence}");
            holder.transform.SetPositionAndRotation(camera.Position, camera.Rotation);
            var own = holder.AddComponent<CinemachineVirtualCamera>();
            own.m_Lens = lensOf != null ? lensOf.m_Lens : LensSettings.Default;
            return new OwnCamera(holder, own);
        }

        public void Apply(ModCamera camera, int priority)
        {
            var follow = camera.Follow;
            if (follow != null)
            {
                var at = follow.position + camera.FollowOffset;
                camera.Position = at;
            }
            var look = camera.LookAt;
            if (look != null)
            {
                var direction = look.position - camera.Position;
                if (direction.sqrMagnitude > 1e-6f) camera.Rotation = Quaternion.LookRotation(direction);
            }
            _holder.transform.SetPositionAndRotation(camera.Position, camera.Rotation);
            var lens = _camera.m_Lens;
            float fov = float.IsFinite(camera.FieldOfView) ? Math.Clamp(camera.FieldOfView, 1f, 179f) : lens.FieldOfView;
            if (lens.FieldOfView != fov)
            {
                lens.FieldOfView = fov;
                _camera.m_Lens = lens;
            }
            if (_camera.Priority != priority) _camera.Priority = priority;
        }

        public void Leave(int priority)
        {
            if (IsAlive) _camera.Priority = priority;
        }

        public void Destroy()
        {
            if (_holder != null) Object.Destroy(_holder);
        }
    }
}
