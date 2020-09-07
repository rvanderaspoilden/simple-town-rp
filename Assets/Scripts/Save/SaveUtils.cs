using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
            transformData.scale = new Vector3Data(transform.localScale);
            return transformData;
        }

        public static DoorTeleporterData CreateDoorTeleporterData(DoorTeleporter doorTeleporter) {
            DoorTeleporterData doorTeleporterData = new DoorTeleporterData();
            doorTeleporterData.Init(doorTeleporter);
            doorTeleporterData.destination = doorTeleporter.GetDestination().ToString();
            doorTeleporterData.doorDirection = doorTeleporter.GetDoorDirection().ToString();
            return doorTeleporterData;
        }

        public static ElevatorTeleporterData CreateElevatorTeleporterData(ElevatorTeleporter elevatorTeleporter) {
            ElevatorTeleporterData data = new ElevatorTeleporterData();
            data.Init(elevatorTeleporter);
            data.destination = elevatorTeleporter.GetDestination().ToString();
            return data;
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
            return data;
        }

        public static DefaultData CreateDefaultData(Props props) {
            DefaultData data = new DefaultData();
            data.Init(props);
            return data;
        }

        public static PackageData CreatePackageData(Package package) {
            PackageData data = new PackageData();
            data.Init(package);
            data.propsConfigIdInside = package.GetPropsInside().GetId();
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

        public static Props InstantiatePropsFromSave(DefaultData data) {
            PropsConfig propsConfig = DatabaseManager.PropsDatabase.GetPropsById(data.id);
            Props props = PropsManager.instance.InstantiateProps(propsConfig, true);
            props.transform.localPosition = data.transform.position.ToVector3();
            props.transform.localEulerAngles = data.transform.rotation.ToVector3();

            if (propsConfig.MustBeBuilt()) {
                props.SetIsBuilt(data.isBuilt);
            }

            return props;
        }
    }
}