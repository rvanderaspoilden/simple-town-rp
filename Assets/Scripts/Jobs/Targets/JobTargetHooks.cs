using System.Collections.Generic;
using Sim;
using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Helpers de (dé)enregistrement des cibles dans JobTargetRegistry.
    /// Centralisé ici pour qu'aucune classe gameplay (PlayerController,
    /// NpcAIController, …) ne référence directement les types d'adapter.
    ///
    /// Points d'appel attendus (serveur uniquement) :
    ///   PlayerController.OnStartServer  → JobTargetHooks.RegisterPlayer(this)
    ///   PlayerController.OnStopServer   → JobTargetHooks.UnregisterPlayer(this)
    ///   NpcAIController.OnEnable        → JobTargetHooks.RegisterNpc(this, _npcId)   [après l'appel à NpcServerManager.Register]
    ///   NpcAIController.OnDisable       → JobTargetHooks.UnregisterNpc(this)
    /// </summary>
    public static class JobTargetHooks {
        private static readonly Dictionary<PlayerController, PlayerJobTarget> _playerTargets =
            new Dictionary<PlayerController, PlayerJobTarget>();

        private static readonly Dictionary<NpcAIController, NpcJobTarget> _npcTargets =
            new Dictionary<NpcAIController, NpcJobTarget>();

        public static void RegisterPlayer(PlayerController player) {
            if (player == null || _playerTargets.ContainsKey(player)) return;
            var target = new PlayerJobTarget(player);
            _playerTargets[player] = target;
            JobTargetRegistry.Instance.Register(target);
        }

        public static void UnregisterPlayer(PlayerController player) {
            if (player == null) return;
            if (!_playerTargets.TryGetValue(player, out var target)) return;
            JobTargetRegistry.Instance.Unregister(target);
            _playerTargets.Remove(player);
            JobServerManager.Instance.OnPlayerDisconnected(player.netId);
            JobBoardServer.Instance.OnPlayerDisconnected(player.connectionToClient);
        }

        public static void RegisterNpc(NpcAIController npc, int npcId) {
            if (npc == null || npcId <= 0) return;
            UnregisterNpc(npc);
            var target = new NpcJobTarget(npc, npcId);
            _npcTargets[npc] = target;
            JobTargetRegistry.Instance.Register(target);
        }

        public static void UnregisterNpc(NpcAIController npc) {
            if (npc == null) return;
            if (!_npcTargets.TryGetValue(npc, out var target)) return;
            JobTargetRegistry.Instance.Unregister(target);
            _npcTargets.Remove(npc);
        }
    }
}
