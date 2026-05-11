using System;
using Sim.Building;
using Sim.Utils;
using UnityEngine;

namespace Sim {
    [Serializable]
    public struct SceneData {
        /// <summary>Bump this when changing the save format. Read-side migrations branch on this value.</summary>
        public const int CurrentSchemaVersion = 1;

        // Schema version of this serialized payload. A value of 0 (the default for older
        // saves missing the field) is treated as "v0 — pre-versioning".
        public int           schemaVersion;
        public CoverData[]   walls;
        public CoverData[]   grounds;
        public BucketData[]  buckets;
        public DefaultData[] props;
        public DefaultData[] lights;
        public DoorData[]    doors;
    }

    /// <summary>
    /// Persisted form of an apartment prop. Built from a ServerPropState in the
    /// new prop system (no NetworkBehaviour involved).
    /// </summary>
    [Serializable]
    public class DefaultData {
        public int           id;          // PropsConfig.GetId()
        public TransformData transform;
        public bool          isBuilt;
        public int           presetId = -1;

        public DefaultData() { }

        /// <summary>Initialize from a ServerPropState (new system).</summary>
        public DefaultData(ServerPropState state, Transform parent) {
            this.id = state.PrefabId;

            // Convert world-space pos/rot stored in ServerPropState to local-space
            // relative to the apartment props container — this matches the legacy
            // save format and how ApartmentController re-applies positions on load.
            Vector3 localPos = parent != null ? parent.InverseTransformPoint(state.Position) : state.Position;
            Quaternion localRot = parent != null ? Quaternion.Inverse(parent.rotation) * state.Rotation : state.Rotation;

            this.transform = new TransformData {
                position = new Vector3Data(localPos),
                rotation = new Vector3Data(localRot.eulerAngles)
            };

            PropStateHeader header = PropStateHeader.ReadFrom(state.Payload);
            this.isBuilt  = header.IsBuilt;
            this.presetId = header.PresetId;
        }
    }

    [Serializable]
    public class BucketData : DefaultData {
        public float[] color;
        public int     paintConfigId;

        public BucketData() { }

        public BucketData(ServerPropState state, Transform parent) : base(state, parent) {
            PaintBucketState bucketState = PaintBucketState.Deserialize(state.Payload);
            this.paintConfigId = bucketState.PaintConfigId;
            this.color         = new[] { bucketState.R, bucketState.G, bucketState.B, 1f };
        }
    }

    /// <summary>
    /// Persisted door state. Front door = doorNumber > 0; inner doors = doorNumber 0.
    /// lockState is stored as int (0=LOCKED, 1=UNLOCKED) for JsonUtility compatibility.
    /// </summary>
    [Serializable]
    public class DoorData : DefaultData {
        public int  lockState;
        public bool isOpen;
        public int  doorNumber;

        public DoorData() { }

        public DoorData(ServerPropState state, Transform parent) : base(state, parent) {
            DoorState ds   = DoorState.Deserialize(state.Payload);
            this.lockState = (int)ds.LockState;
            this.isOpen    = ds.IsOpen;
            this.doorNumber = ds.DoorNumber;
        }
    }

    [Serializable]
    public struct CoverData {
        public int     idx;
        public int     paintConfigId;
        public float[] additionalColor;

        public Color GetColor() {
            if (additionalColor.Length > 3) {
                return new Color(additionalColor[0], additionalColor[1], additionalColor[2]);
            }

            return Color.white;
        }
    }

    [Serializable]
    public class TransformData {
        public Vector3Data position;
        public Vector3Data rotation;
    }

    [Serializable]
    public class Vector3Data {
        public float x, y, z;

        public Vector3Data() { }

        public Vector3Data(float x, float y, float z) {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public Vector3Data(Vector3 vector3) {
            this.x = vector3.x;
            this.y = vector3.y;
            this.z = vector3.z;
        }

        public Vector3 ToVector3() {
            return new Vector3(this.x, this.y, this.z);
        }
    }
}
