using System;
using Newtonsoft.Json;
using Sim.Enums;
using UnityEngine;

namespace Sim {
    [Serializable]
    public class SceneData {
        public DoorTeleporterData[] doorTeleporters;
        public ElevatorTeleporterData[] elevatorTeleporters;
    }

    [Serializable]
    public class DefaultData {
        public int id;
        public TransformData transform;
    }

    [Serializable]
    public class DoorTeleporterData : DefaultData {
        public String destination;
        public String doorDirection;
    }

    [Serializable]
    public class ElevatorTeleporterData : DefaultData {
        public String destination;
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