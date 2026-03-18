using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("AlwaysPoweredCameras", "NetworkLuki", "1.0.0")]
    [Description("All CCTV cameras always have power without electricity")]

    public class AlwaysPoweredCameras : RustPlugin
    {
        void OnEntitySpawned(BaseNetworkable entity)
        {
            var camera = entity as CCTV_RC;
            if (camera == null) return;

            NextTick(() =>
            {
                ForcePower(camera);
            });
        }

        void ForcePower(CCTV_RC camera)
        {
            if (camera == null || camera.IsDestroyed) return;

            var io = camera as IOEntity;

            if (io != null)
            {
                io.UpdateHasPower(10, 0);
                io.MarkDirty();
            }
        }

        void OnEntityLoaded(BaseNetworkable entity)
        {
            var camera = entity as CCTV_RC;
            if (camera == null) return;

            ForcePower(camera);
        }

        object OnIORefCleared(IOEntity entity)
        {
            var camera = entity as CCTV_RC;
            if (camera == null) return null;

            ForcePower(camera);
            return false;
        }
    }
}