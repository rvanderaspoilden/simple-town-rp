using Sim;
using UnityEngine;

namespace Sim.Missions {
    /// <summary>
    /// Adapter IMissionTarget pour un joueur. POCO, créé à l'enregistrement et
    /// jeté au désenregistrement. La référence PlayerController peut survivre
    /// après une déconnexion ; IsAvailable se base sur la connexion réseau.
    /// </summary>
    public sealed class PlayerMissionTarget : IMissionTarget {
        private readonly PlayerController player;

        public PlayerMissionTarget(PlayerController player) {
            this.player = player;
        }

        public string TargetId => player != null ? player.netId.ToString() : string.Empty;
        public MissionTargetKind Kind => MissionTargetKind.Player;

        public Transform Transform => player != null ? player.transform : null;

        public bool IsAvailable =>
            player != null
            && player.netIdentity != null
            && player.netIdentity.connectionToClient != null;

        public string DisplayName {
            get {
                if (player == null || player.CharacterData == null) return string.Empty;
                var fullName = player.CharacterData.Identity.FullName;
                return string.IsNullOrEmpty(fullName) ? string.Empty : fullName;
            }
        }
    }
}
