using System.Collections.Generic;
using System.Linq;
using Mirror;
using Sim.Building;
using UnityEngine;

namespace Sim.Utils {
    public static class SaveUtils {
        public static TransformData CreateTransformData(Transform transform) {
            TransformData transformData = new TransformData();
            transformData.position = new Vector3Data(transform.localPosition);
            transformData.rotation = new Vector3Data(transform.localEulerAngles);
            return transformData;
        }

        public static CoverData[] CreateCoverDatas(Dictionary<int, CoverSettings> settings) {
            return settings.Select(pair => new CoverData {
                idx = pair.Key,
                additionalColor = pair.Value.GetColor(),
                paintConfigId = pair.Value.paintConfigId
            }).ToArray();
        }

        /// <summary>
        /// Spawns a prop in the new system from a saved DefaultData entry.
        /// Returns the assigned propId (or -1 on failure).
        /// </summary>
        [Server]
        public static int SpawnPropFromSave(DefaultData data, ApartmentController parent) {
            // Reconstruct world-space pos/rot from local-space saved values
            Transform container = parent.PropsContainer;
            Vector3    localPos = data.transform.position.ToVector3();
            Quaternion localRot = Quaternion.Euler(data.transform.rotation.ToVector3());

            Vector3    worldPos = container != null ? container.TransformPoint(localPos) : localPos;
            Quaternion worldRot = container != null ? container.rotation * localRot     : localRot;

            // Build initial payload from saved state — only override the full payload for
            // PaintBucket (paint config + color need to come from the save). For every other
            // type we let the ServerPropSource emit the type-specific body (seat slots,
            // delivery count, ...) and just override the header (isBuilt + presetId).
            PropStateHeader header = new PropStateHeader { IsBuilt = data.isBuilt, PresetId = data.presetId };

            byte[] payload = null;
            if (data is BucketData bucket) {
                payload = new PaintBucketState {
                    Header        = header,
                    PaintConfigId = bucket.paintConfigId,
                    R = bucket.color != null && bucket.color.Length > 0 ? bucket.color[0] : 1f,
                    G = bucket.color != null && bucket.color.Length > 1 ? bucket.color[1] : 1f,
                    B = bucket.color != null && bucket.color.Length > 2 ? bucket.color[2] : 1f
                }.Serialize();
            }

            int propId = ServerPropManager.Instance.SpawnProp(
                parent.RoomId, data.id, worldPos, worldRot,
                initialPayloadOverride: payload,
                headerOverride:         header
            );

            if (propId >= 0) {
                parent.TrackProp(propId);
                GameObject go = ServerPropManager.Instance.GetSpawnedGameObject(propId);
                if (go != null && container != null) {
                    go.transform.SetParent(container);
                    go.transform.position = worldPos;
                    go.transform.rotation = worldRot;
                }
            }

            return propId;
        }
    }
}
