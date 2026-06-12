using System.Collections.Generic;
using Sim.Logging;
using UnityEngine;

namespace Sim.Missions {
    /// <summary>
    /// Registre serveur des cibles potentielles de mission (joueurs, NPC,
    /// appartements, boîtes aux lettres…). Les adapters IMissionTarget s'y
    /// enregistrent à leur spawn et se désenregistrent au despawn.
    ///
    /// Vit côté serveur uniquement, plain C# singleton — même pattern que
    /// NpcServerManager. Aucune dépendance Mirror : le registre ne broadcast
    /// rien lui-même, c'est le rôle de MissionServerManager.
    ///
    /// Lookup générique par (kind, id) + lookups typés rapides pour les cas
    /// les plus courants (joueur par netId, NPC par id).
    /// </summary>
    public class MissionTargetRegistry {
        private static MissionTargetRegistry _instance;
        public static MissionTargetRegistry Instance => _instance ??= new MissionTargetRegistry();

        private readonly Dictionary<(MissionTargetKind, string), IMissionTarget> _byKey =
            new Dictionary<(MissionTargetKind, string), IMissionTarget>();

        private readonly Dictionary<uint, IMissionTarget> _playersByNetId =
            new Dictionary<uint, IMissionTarget>();

        private readonly Dictionary<int, IMissionTarget> _npcsById =
            new Dictionary<int, IMissionTarget>();

        public void Reset() {
            int count = _byKey.Count;
            _byKey.Clear();
            _playersByNetId.Clear();
            _npcsById.Clear();
            GameLogger.System.Info("MissionTargetRegistryReset {Count}", count);
        }

        public void Register(IMissionTarget target) {
            if (target == null || string.IsNullOrEmpty(target.TargetId)) return;
            var key = (target.Kind, target.TargetId);
            _byKey[key] = target;

            switch (target.Kind) {
                case MissionTargetKind.Player when uint.TryParse(target.TargetId, out var netId):
                    _playersByNetId[netId] = target;
                    break;
                case MissionTargetKind.Npc when int.TryParse(target.TargetId, out var npcId):
                    _npcsById[npcId] = target;
                    break;
            }
        }

        public void Unregister(IMissionTarget target) {
            if (target == null || string.IsNullOrEmpty(target.TargetId)) return;
            var key = (target.Kind, target.TargetId);
            _byKey.Remove(key);

            switch (target.Kind) {
                case MissionTargetKind.Player when uint.TryParse(target.TargetId, out var netId):
                    _playersByNetId.Remove(netId);
                    break;
                case MissionTargetKind.Npc when int.TryParse(target.TargetId, out var npcId):
                    _npcsById.Remove(npcId);
                    break;
            }
        }

        public bool TryGet(MissionTargetKind kind, string id, out IMissionTarget target)
            => _byKey.TryGetValue((kind, id), out target);

        public IMissionTarget GetPlayer(uint netId)
            => _playersByNetId.TryGetValue(netId, out var t) ? t : null;

        public IMissionTarget GetNpc(int npcId)
            => _npcsById.TryGetValue(npcId, out var t) ? t : null;

        public Vector3? TryGetPlayerPosition(uint netId) {
            var t = GetPlayer(netId);
            return t != null && t.IsAvailable ? t.Transform.position : (Vector3?)null;
        }

        public IEnumerable<IMissionTarget> All => _byKey.Values;
    }
}
