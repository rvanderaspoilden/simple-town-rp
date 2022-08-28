using System;
using Mirror;
using UnityEngine;

namespace Sim.Utils {
    public class NetworkUtils {
        public static GameObject FindObject(uint netId, float timeout = 2f) {
            float time = Time.time;

            while (!NetworkIdentity.spawned.ContainsKey(netId) && Time.time < time + timeout) {
                Debug.Log($"Searching network object with ID {netId}");
            }

            if (NetworkIdentity.spawned.ContainsKey(netId)) {
                return NetworkIdentity.spawned[netId].gameObject;
            }

            throw new Exception($"Network object with ID {netId} not found");
        }
    }
}