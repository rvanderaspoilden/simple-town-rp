using System;
using Sim.Building;
using Sim.Utils;
using UnityEngine;

namespace Sim {
    [Serializable]
    public struct SceneData {
        public CoverData[] walls;
        public CoverData[] grounds;
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
    public struct CoverData {
        public int idx;
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