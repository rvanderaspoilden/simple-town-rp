using System.Collections.Generic;
using Sim.Logging;
using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Registre serveur des cibles potentielles de mission (joueurs, NPC,
    /// appartements, boîtes aux lettres…). Les adapters IJobTarget s'y
    /// enregistrent à leur spawn et se désenregistrent au despawn.
    ///
    /// Vit côté serveur uniquement, plain C# singleton — même pattern que
    /// NpcServerManager. Aucune dépendance Mirror : le registre ne broadcast
    /// rien lui-même, c'est le rôle de JobServerManager.
    ///
    /// Lookup générique par (kind, id) + lookups typés rapides pour les cas
    /// les plus courants (joueur par netId, NPC par id).
    /// </summary>
    public class JobTargetRegistry {
        private static JobTargetRegistry _instance;
        public static JobTargetRegistry Instance => _instance ??= new JobTargetRegistry();

        private readonly Dictionary<(JobTargetKind, string), IJobTarget> _byKey =
            new Dictionary<(JobTargetKind, string), IJobTarget>();

        private readonly Dictionary<uint, IJobTarget> _playersByNetId =
            new Dictionary<uint, IJobTarget>();

        private readonly Dictionary<int, IJobTarget> _npcsById =
            new Dictionary<int, IJobTarget>();

        public void Reset() {
            int count = _byKey.Count;
            _byKey.Clear();
            _playersByNetId.Clear();
            _npcsById.Clear();
            GameLogger.System.Info("JobTargetRegistryReset {Count}", count);
        }

        public void Register(IJobTarget target) {
            if (target == null || string.IsNullOrEmpty(target.TargetId)) return;
            var key = (target.Kind, target.TargetId);
            _byKey[key] = target;

            switch (target.Kind) {
                case JobTargetKind.Player when uint.TryParse(target.TargetId, out var netId):
                    _playersByNetId[netId] = target;
                    break;
                case JobTargetKind.Npc when int.TryParse(target.TargetId, out var npcId):
                    _npcsById[npcId] = target;
                    break;
            }
        }

        public void Unregister(IJobTarget target) {
            if (target == null || string.IsNullOrEmpty(target.TargetId)) return;
            var key = (target.Kind, target.TargetId);
            _byKey.Remove(key);

            switch (target.Kind) {
                case JobTargetKind.Player when uint.TryParse(target.TargetId, out var netId):
                    _playersByNetId.Remove(netId);
                    break;
                case JobTargetKind.Npc when int.TryParse(target.TargetId, out var npcId):
                    _npcsById.Remove(npcId);
                    break;
            }
        }

        public bool TryGet(JobTargetKind kind, string id, out IJobTarget target)
            => _byKey.TryGetValue((kind, id), out target);

        public IJobTarget GetPlayer(uint netId)
            => _playersByNetId.TryGetValue(netId, out var t) ? t : null;

        public IJobTarget GetNpc(int npcId)
            => _npcsById.TryGetValue(npcId, out var t) ? t : null;

        public Vector3? TryGetPlayerPosition(uint netId) {
            var t = GetPlayer(netId);
            return t != null && t.IsAvailable ? t.Transform.position : (Vector3?)null;
        }

        public IEnumerable<IJobTarget> All => _byKey.Values;
    }
}
