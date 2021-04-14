using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Sim.Building;
using Sim.Utils;
using UnityEngine;

namespace Sim {
    [Serializable]
    public class SceneData {
        public DoorTeleporterData[] doorTeleporters;
        public ElevatorTeleporterData[] elevatorTeleporters;
        public WallData walls;
        public DoorData[] simpleDoors;
        public GroundData[] grounds;
        public PackageData[] packages;
        public BucketData[] buckets;
        public DefaultData[] props;
    }

    [Serializable]
    public class DefaultData {
        public int id;
        public TransformData transform;
        public bool isBuilt;
        public int presetId = -1;

        public void Init(Props props) {
            this.id = props.GetConfiguration().GetId();
            this.transform = SaveUtils.CreateTransformData(props.transform);
            this.isBuilt = props.IsBuilt();
            this.presetId = props.PresetId;
        }
    }

    [Serializable]
    public class BucketData : DefaultData {
        public float[] color;
        public int paintConfigId;
        
        public BucketData() { }

        public BucketData(Props props) {
            base.Init(props);
        }
    }

    [Serializable]
    public class PackageData : DefaultData {
        public int propsConfigIdInside;
        
        public PackageData() { }

        public PackageData(Props props) {
            base.Init(props);
        }
    }

    [Serializable]
    public class DoorTeleporterData : DefaultData {
        public String destination;
        public String doorDirection;
        public int number;
        
        public DoorTeleporterData() { }

        public DoorTeleporterData(Props props) {
            base.Init(props);
        }
    }

    [Serializable]
    public class DoorData : DefaultData {
        public DoorData() { }

        public DoorData(Props props) {
            base.Init(props);
        }
    }

    [Serializable]
    public class GroundData : DefaultData {
        public int paintConfigId;
        public GroundData() { }

        public GroundData(Props props) {
            base.Init(props);
        }
    }

    [Serializable]
    public class ElevatorTeleporterData : DefaultData {
        public String destination;
        
        public ElevatorTeleporterData() { }

        public ElevatorTeleporterData(Props props) {
            base.Init(props);
        }
    }

    [Serializable]
    public class WallData {
        public WallFaceData[] wallFaces;
        
        public WallData() { }

        public WallData(Dictionary<int, CoverSettings> settings) {
            wallFaces = settings.Select(pair => new WallFaceData {
                sharedMaterialIdx = pair.Key,
                additionalColor = pair.Value.GetColor(),
                paintConfigId = pair.Value.paintConfigId
            }).ToArray();
        }
        
        public WallData(SyncDictionary<int, CoverSettings> settings) {
            wallFaces = settings.Select(pair => new WallFaceData {
                sharedMaterialIdx = pair.Key,
                additionalColor = pair.Value.GetColor(),
                paintConfigId = pair.Value.paintConfigId
            }).ToArray();
        }
    }

    [Serializable]
    public struct WallFaceData {
        public int sharedMaterialIdx;
        public int paintConfigId;
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