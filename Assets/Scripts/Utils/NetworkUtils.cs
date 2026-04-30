using System;
using Mirror;
using UnityEngine;

namespace Sim.Utils {
    public class NetworkUtils {
        public static GameObject FindObject(uint netId, float timeout = 2f) {
            float time = Time.time;

            while (!NetworkClient.spawned.ContainsKey(netId) && Time.time < time + timeout) {
                Debug.Log($"Searching network object with ID {netId}");
            }

            if (NetworkClient.spawned.ContainsKey(netId)) {
                return NetworkClient.spawned[netId].gameObject;
            }

            throw new Exception($"Network object with ID {netId} not found");
        }
    }
}