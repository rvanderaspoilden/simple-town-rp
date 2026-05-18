using Sim;
using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Adapter IJobTarget pour un joueur. POCO, créé à l'enregistrement et
    /// jeté au désenregistrement. La référence PlayerController peut survivre
    /// après une déconnexion ; IsAvailable se base sur la connexion réseau.
    /// </summary>
    public sealed class PlayerJobTarget : IJobTarget {
        private readonly PlayerController player;

        public PlayerJobTarget(PlayerController player) {
            this.player = player;
        }

        public string TargetId => player != null ? player.netId.ToString() : string.Empty;
        public JobTargetKind Kind => JobTargetKind.Player;

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
