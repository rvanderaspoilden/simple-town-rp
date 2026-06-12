using System.Collections.Generic;
using Sim;
using UnityEngine;

namespace Sim.Missions {
    /// <summary>
    /// Helpers de (dé)enregistrement des cibles dans MissionTargetRegistry.
    /// Centralisé ici pour qu'aucune classe gameplay (PlayerController,
    /// NpcAIController, …) ne référence directement les types d'adapter.
    ///
    /// Points d'appel attendus (serveur uniquement) :
    ///   PlayerController.OnStartServer  → MissionTargetHooks.RegisterPlayer(this)
    ///   PlayerController.OnStopServer   → MissionTargetHooks.UnregisterPlayer(this)
    ///   NpcAIController.OnEnable        → MissionTargetHooks.RegisterNpc(this, _npcId)   [après l'appel à NpcServerManager.Register]
    ///   NpcAIController.OnDisable       → MissionTargetHooks.UnregisterNpc(this)
    /// </summary>
    public static class MissionTargetHooks {
        private static readonly Dictionary<PlayerController, PlayerMissionTarget> _playerTargets =
            new Dictionary<PlayerController, PlayerMissionTarget>();

        private static readonly Dictionary<NpcAIController, NpcMissionTarget> _npcTargets =
            new Dictionary<NpcAIController, NpcMissionTarget>();

        public static void RegisterPlayer(PlayerController player) {
            if (player == null || _playerTargets.ContainsKey(player)) return;
            var target = new PlayerMissionTarget(player);
            _playerTargets[player] = target;
            MissionTargetRegistry.Instance.Register(target);
        }

        public static void UnregisterPlayer(PlayerController player) {
            if (player == null) return;
            if (!_playerTargets.TryGetValue(player, out var target)) return;
            MissionTargetRegistry.Instance.Unregister(target);
            _playerTargets.Remove(player);
            MissionServerManager.Instance.OnPlayerDisconnected(player.netId);
            MissionBoardServer.Instance.OnPlayerDisconnected(player.connectionToClient);
        }

        public static void RegisterNpc(NpcAIController npc, int npcId) {
            if (npc == null || npcId <= 0) return;
            UnregisterNpc(npc);
            var target = new NpcMissionTarget(npc, npcId);
            _npcTargets[npc] = target;
            MissionTargetRegistry.Instance.Register(target);
        }

        public static void UnregisterNpc(NpcAIController npc) {
            if (npc == null) return;
            if (!_npcTargets.TryGetValue(npc, out var target)) return;
            MissionTargetRegistry.Instance.Unregister(target);
            _npcTargets.Remove(npc);
        }
    }
}
