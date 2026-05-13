using System.Collections.Generic;
using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Sac à dos partagé entre les steps d'une mission. Stocke un nombre
    /// arbitraire de cibles nommées (clés libres : "primary", "secondary",
    /// "trash", "cart", "shop", …) + un blackboard générique key→object.
    /// Vit côté serveur uniquement.
    ///
    /// Les accesseurs primaryTarget/secondaryTarget restent disponibles
    /// comme raccourcis pour les missions "à deux points" — ils mappent
    /// simplement vers les clés "primary" et "secondary" du dict.
    /// </summary>
    public class JobContext {
        private readonly Dictionary<string, IJobTarget> _targets = new Dictionary<string, IJobTarget>();
        private readonly Dictionary<string, object> _blackboard = new Dictionary<string, object>();

        public string payloadItemId;
        public Vector3? waypoint;

        public const string PrimaryKey   = "primary";
        public const string SecondaryKey = "secondary";

        // ── Cibles nommées ─────────────────────────────────────────────

        public void SetTarget(string key, IJobTarget target) {
            if (string.IsNullOrEmpty(key)) return;
            if (target == null) _targets.Remove(key);
            else _targets[key] = target;
        }

        public IJobTarget TargetByKey(string key) {
            if (string.IsNullOrEmpty(key)) key = PrimaryKey;
            return _targets.TryGetValue(key, out var t) ? t : null;
        }

        public IJobTarget TargetByKey(JobTargetKey key) => TargetByKey(key.ToKey());

        public void SetTarget(JobTargetKey key, IJobTarget target) => SetTarget(key.ToKey(), target);

        public bool HasTarget(string key) => _targets.ContainsKey(key);
        public IEnumerable<KeyValuePair<string, IJobTarget>> Targets => _targets;

        public IJobTarget primaryTarget {
            get => TargetByKey(PrimaryKey);
            set => SetTarget(PrimaryKey, value);
        }

        public IJobTarget secondaryTarget {
            get => TargetByKey(SecondaryKey);
            set => SetTarget(SecondaryKey, value);
        }

        // ── Blackboard générique (données libres) ──────────────────────

        public T Get<T>(string key) where T : class
            => _blackboard.TryGetValue(key, out var v) ? v as T : null;

        public bool TryGetStruct<T>(string key, out T value) where T : struct {
            if (_blackboard.TryGetValue(key, out var v) && v is T t) { value = t; return true; }
            value = default;
            return false;
        }

        public void Set(string key, object value) => _blackboard[key] = value;
        public bool Has(string key) => _blackboard.ContainsKey(key);
    }
}
