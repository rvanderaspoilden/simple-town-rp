using DG.Tweening;
using Sim;

namespace AI.States {
    /// <summary>
    /// État LOCAL du joueur renversé par un véhicule (ragdoll). Verrouille le déplacement
    /// (NavMeshAgent + collider désactivés) comme <see cref="CharacterDrive"/>. Le ragdoll VISUEL
    /// est piloté par <see cref="PlayerController"/> (SyncVar hook + ClientRpc) sur TOUS les clients,
    /// donc cet état n'y touche pas — il ne gère que le verrouillage local de l'input/déplacement.
    /// La relève (repositionnement racine + retour Idle) est déclenchée par le hook côté owner.
    /// </summary>
    public class CharacterKnockdown : IState {
        private readonly PlayerController player;

        public CharacterKnockdown(PlayerController player) {
            this.player = player;
        }

        public void OnEnter() {
            this.player.transform.DOComplete(); // stoppe un éventuel tween LookAt
            this.player.NavMeshAgent.enabled = false;
            this.player.Collider.enabled = false;
            HUDManager.Instance.CloseContextMenu();
            HUDManager.Instance.CloseInventory();
        }

        public void Tick() { }

        public void OnExit() {
            this.player.Collider.enabled = true;
            this.player.NavMeshAgent.enabled = true;
        }
    }
}
