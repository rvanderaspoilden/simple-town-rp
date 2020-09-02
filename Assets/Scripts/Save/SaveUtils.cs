using System.Collections;
using System.Collections.Generic;
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

        public static DoorTeleporterData CreateDoorTeleporterData(int id, DoorTeleporter doorTeleporter) {
            DoorTeleporterData doorTeleporterData = new DoorTeleporterData();
            doorTeleporterData.id = id;
            doorTeleporterData.transform = CreateTransformData(doorTeleporter.transform);
            doorTeleporterData.destination = doorTeleporter.GetDestination().ToString();
            doorTeleporterData.doorDirection = doorTeleporter.GetDoorDirection().ToString();
            return doorTeleporterData;
        }

        public static ElevatorTeleporterData CreateElevatorTeleporterData(int id, ElevatorTeleporter elevatorTeleporter) {
            ElevatorTeleporterData data = new ElevatorTeleporterData();
            data.id = id;
            data.transform = CreateTransformData(elevatorTeleporter.transform);
            data.destination = elevatorTeleporter.GetDestination().ToString();
            return data;
        }

        public static Props InstantiatePropsFromSave(DefaultData data, Transform container) {
            PropsConfig doorConfig = DatabaseManager.PropsDatabase.GetPropsById(data.id);
            Props props = PropsManager.instance.InstantiateProps(doorConfig, true);
            props.transform.parent = container;
            props.transform.localPosition = data.transform.position.ToVector3();
            props.transform.localEulerAngles = data.transform.rotation.ToVector3();
            return props;
        }
    }
}