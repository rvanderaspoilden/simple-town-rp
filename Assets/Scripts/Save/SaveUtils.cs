using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Photon.Pun;
using Sim.Building;
using Sim.Interactables;
using Sim.Scriptables;
using UnityEngine;

namespace Sim.Utils {
    public static class SaveUtils {
        public static TransformData CreateTransformData(Transform transform) {
            TransformData transformData = new TransformData();
            transformData.position = new Vector3Data(transform.localPosition);
            transformData.rotation = new Vector3Data(transform.localEulerAngles);
            return transformData;
        }

        public static WallData CreateWallData(Wall wall) {
            WallData data = new WallData();
            data.Init(wall);
            data.wallFaces = wall.GetWallFaces().Select(face => new WallFaceData(face)).ToArray();
            return data;
        }

        public static DoorData CreateDoorData(SimpleDoor door) {
            DoorData data = new DoorData();
            data.Init(door);
            return data;
        }

        public static GroundData CreateGroundData(Ground ground) {
            GroundData data = new GroundData();
            data.Init(ground);
            data.paintConfigId = ground.GetPaintConfigId();
            return data;
        }

        public static DefaultData CreateDefaultData(Props props) {
            DefaultData data = new DefaultData();
            data.Init(props);
            return data;
        }

        public static BucketData CreateBucketData(PaintBucket paintBucket) {
            BucketData data = new BucketData();
            data.Init(paintBucket);
            data.paintConfigId = paintBucket.GetPaintConfig().GetId();

            if (paintBucket.GetPaintConfig().AllowCustomColor()) {
                data.color = new float[4] {paintBucket.GetColor().r, paintBucket.GetColor().g, paintBucket.GetColor().b, paintBucket.GetColor().a};
            }

            return data;
        }

        [Server]
        public static Props InstantiatePropsFromSave(DefaultData data, Vector3 relativeOrigin) {
            PropsConfig propsConfig = DatabaseManager.PropsDatabase.GetPropsById(data.id);
            Props props = PropsManager.Instance.InstantiateProps(propsConfig, data.presetId, relativeOrigin + data.transform.position.ToVector3(), Quaternion.Euler(data.transform.rotation.ToVector3()), false);

            props.InitBuilt(!propsConfig.MustBeBuilt() || data.isBuilt);

            NetworkServer.Spawn(props.gameObject);

            return props;
        }
    }
}