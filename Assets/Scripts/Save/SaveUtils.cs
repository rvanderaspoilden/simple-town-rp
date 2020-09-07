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
            doorTeleporterData.id = doorTeleporter.GetConfiguration().GetId();
            doorTeleporterData.transform = CreateTransformData(doorTeleporter.transform);
            doorTeleporterData.destination = doorTeleporter.GetDestination().ToString();
            doorTeleporterData.doorDirection = doorTeleporter.GetDoorDirection().ToString();
            return doorTeleporterData;
        }

        public static ElevatorTeleporterData CreateElevatorTeleporterData(ElevatorTeleporter elevatorTeleporter) {
            ElevatorTeleporterData data = new ElevatorTeleporterData();
            data.id = elevatorTeleporter.GetConfiguration().GetId();
            data.transform = CreateTransformData(elevatorTeleporter.transform);
            data.destination = elevatorTeleporter.GetDestination().ToString();
            return data;
        }
        
        public static WallData CreateWallData(Wall wall) {
            WallData data = new WallData();
            data.id = wall.GetConfiguration().GetId();
            data.transform = CreateTransformData(wall.transform);
            data.wallFaces = wall.GetWallFaces().Select(face => new WallFaceData(face)).ToArray();
            return data;
        }
        
        public static DoorData CreateDoorData(SimpleDoor door) {
            DoorData data = new DoorData();
            data.id = door.GetConfiguration().GetId();
            data.transform = CreateTransformData(door.transform);
            return data;
        }
        
        public static GroundData CreateGroundData(Ground ground) {
            GroundData data = new GroundData();
            data.id = ground.GetConfiguration().GetId();
            data.transform = CreateTransformData(ground.transform);
            return data;
        }
        
        public static DefaultData CreateDefaultData(Props props) {
            DefaultData data = new DefaultData();
            data.id = props.GetConfiguration().GetId();
            data.transform = CreateTransformData(props.transform);
            return data;
        }

        public static PackageData CreatePackageData(Package package) {
            PackageData data = new PackageData();
            data.id = package.GetConfiguration().GetId();
            data.transform = CreateTransformData(package.transform);
            data.propsConfigIdInside = package.GetPropsInside().GetId();
            return data;
        }

        public static Props InstantiatePropsFromSave(DefaultData data) {
            PropsConfig doorConfig = DatabaseManager.PropsDatabase.GetPropsById(data.id);
            Props props = PropsManager.instance.InstantiateProps(doorConfig, true);
            props.transform.localPosition = data.transform.position.ToVector3();
            props.transform.localEulerAngles = data.transform.rotation.ToVector3();
            return props;
        }
    }
}