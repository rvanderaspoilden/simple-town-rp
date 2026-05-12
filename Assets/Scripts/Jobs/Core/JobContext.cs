using System.Collections.Generic;
using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Sac à dos partagé entre les steps d'une mission. C'est l'unique canal de
    /// communication inter-steps — aucune référence directe d'un step à un autre.
    /// Vit côté serveur uniquement.
    /// </summary>
    public class JobContext {
        public IJobTarget primaryTarget;
        public IJobTarget secondaryTarget;
        public string payloadItemId;
        public Vector3? waypoint;

        private readonly Dictionary<string, object> blackboard = new Dictionary<string, object>();

        public T Get<T>(string key) where T : class
            => blackboard.TryGetValue(key, out var v) ? v as T : null;

        public bool TryGetStruct<T>(string key, out T value) where T : struct {
            if (blackboard.TryGetValue(key, out var v) && v is T t) { value = t; return true; }
            value = default;
            return false;
        }

        public void Set(string key, object value) => blackboard[key] = value;

        public bool Has(string key) => blackboard.ContainsKey(key);

        public IJobTarget TargetByKey(string key) => key switch {
            "secondary" => secondaryTarget,
            _ => primaryTarget
        };
    }
}
