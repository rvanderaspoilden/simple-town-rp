using UnityEngine;

namespace Sim.Missions {
    /// <summary>
    /// Adapter IMissionTarget pour un PNJ. Référence l'NpcAIController (Transform
    /// serveur) + l'id assigné par NpcServerManager. IsAvailable bascule à
    /// false dès que le GameObject est désactivé (cycle NpcPool).
    /// </summary>
    public sealed class NpcMissionTarget : IMissionTarget {
        private readonly NpcAIController npc;
        private readonly int npcId;

        public NpcMissionTarget(NpcAIController npc, int npcId) {
            this.npc = npc;
            this.npcId = npcId;
        }

        public string TargetId => npcId.ToString();
        public MissionTargetKind Kind => MissionTargetKind.Npc;

        public Transform Transform => npc != null ? npc.transform : null;

        public bool IsAvailable =>
            npc != null && npc.gameObject != null && npc.gameObject.activeInHierarchy;

        public string DisplayName => npc != null ? npc.Identity.FullName : string.Empty;

        public int NpcId => npcId;
    }
}
