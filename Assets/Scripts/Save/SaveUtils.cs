using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Sim.Building;
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

        public static CoverData[] CreateCoverDatas(Dictionary<int, CoverSettings> settings) {
            return settings.Select(pair => new CoverData {
                idx = pair.Key,
                additionalColor = pair.Value.GetColor(),
                paintConfigId = pair.Value.paintConfigId
            }).ToArray();
        }
        
        public static CoverData[] CreateCoverDatas(SyncDictionary<int, CoverSettings> settings) {
            return settings.Select(pair => new CoverData {
                idx = pair.Key,
                additionalColor = pair.Value.GetColor(),
                paintConfigId = pair.Value.paintConfigId
            }).ToArray();
        }
        
        public static DefaultData CreateDefaultData(Props props) {
            DefaultData data = new DefaultData();
            data.Init(props);
            return data;
        }

        public static BucketData CreateBucketData(PaintBucket paintBucket) {
            BucketData data = new BucketData();
            data.Init(paintBucket);
            data.paintConfigId = paintBucket.PaintConfigId;
            data.color = CommonUtils.ColorToArray(paintBucket.GetColor());

            return data;
        }

        [Server]
        public static Props InstantiatePropsFromSave(DefaultData data, ApartmentController parent) {
            PropsConfig propsConfig = DatabaseManager.PropsDatabase.GetPropsById(data.id);
            Props props = PropsManager.Instance.InstantiateProps(propsConfig, data.presetId, data.transform.position.ToVector3(),
                Quaternion.Euler(data.transform.rotation.ToVector3()));

            props.InitBuilt(!propsConfig.MustBeBuilt() || data.isBuilt);

            props.transform.SetParent(parent.PropsContainer);
            props.transform.localPosition = data.transform.position.ToVector3();

            props.ParentId = parent.netId;

            NetworkServer.Spawn(props.gameObject);

            return props;
        }
    }
}