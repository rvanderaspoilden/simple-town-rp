using System;
using Newtonsoft.Json;
using Sim.Building;
using Sim.Enums;
using UnityEngine;

namespace Sim {
    [Serializable]
    public class SceneData {
        public DoorTeleporterData[] doorTeleporters;
        public ElevatorTeleporterData[] elevatorTeleporters;
        public WallData[] walls;
        public DoorData[] simpleDoors;
        public GroundData[] grounds;
        public PackageData[] packages;
        public DefaultData[] props;
    }

    [Serializable]
    public class DefaultData {
        public int id;
        public TransformData transform;
    }

    [Serializable]
    public class PackageData : DefaultData {
        public int propsConfigIdInside;
    }

    [Serializable]
    public class DoorTeleporterData : DefaultData {
        public String destination;
        public String doorDirection;
    }

    [Serializable]
    public class DoorData : DefaultData {}

    [Serializable]
    public class GroundData : DefaultData {
    }

    [Serializable]
    public class ElevatorTeleporterData : DefaultData {
        public String destination;
    }

    [Serializable]
    public class WallData : DefaultData {
        public WallFaceData[] wallFaces;
    }

    [Serializable]
    public class WallFaceData {
        public int sharedMaterialIdx;
        public int paintConfigId;
        public float[] additionalColor;

        public WallFaceData() { }

        public WallFaceData(WallFace wallFace) {
            this.sharedMaterialIdx = wallFace.GetSharedMaterialIdx();
            this.paintConfigId = wallFace.GetPaintConfigId();
            this.additionalColor = new float[4] {wallFace.GetAdditionalColor().r, wallFace.GetAdditionalColor().g, wallFace.GetAdditionalColor().b, wallFace.GetAdditionalColor().a};
        }

        public WallFace ToWallFace() {
            Color color = this.additionalColor != null ? new Color(this.additionalColor[0], this.additionalColor[1], this.additionalColor[2], this.additionalColor[3]) : Color.white;
            return new WallFace(this.sharedMaterialIdx, this.paintConfigId, color);
        }
    }

    [Serializable]
    public class TransformData {
        public Vector3Data position;
        public Vector3Data rotation;
        public Vector3Data scale;
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