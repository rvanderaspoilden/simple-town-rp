using Sim;
using Sim.Utils;
using UnityEngine;

namespace AI.States {
    public class CharacterIdle : IState {
        private readonly PlayerController player;

        public CharacterIdle(PlayerController player) {
            this.player = player;
        }

        public void OnEnter() {
            this.player.PlayerState = PlayerState.IDLE;

            var target = this.player.InteractableTarget;
            // Cible mobile (NPC qui marchait, joueur, véhicule) : évaluer la portée contre la position
            // COURANTE de la cible, pas le point figé au clic.
            Vector3 evalPoint = (target.IsAlive())
                ? target.transform.position
                : this.player.InteractionOriginPoint;
            if (!target.IsAlive() || !this.player.CanInteractWith(target, evalPoint)) {
                // Abandon : si on tenait une session NPC sur cette cible (clic puis hors-portée),
                // on la libère côté serveur — sinon le NPC resterait freezé jusqu'au timeout 30 s.
                if (target is ClientNpcView lostNpc && NpcInteractionSession.ActiveNpcId == lostNpc.NpcId) {
                    NpcInteractionSession.End(lostNpc.NpcId);
                }
                this.player.InteractableTarget = null;
                return;
            }

            this.player.LookAt(target.transform);
            HUDManager.Instance.ShowContextMenu(
                target.GetActions(this.player.ShowRadialMenuWithPriority),
                target.transform,
                this.player.ShowRadialMenuWithPriority
            );
            this.player.InteractableTarget = null;
        }

        public void Tick() {
            this.player.Animator.SetVelocity(this.player.NavMeshAgent.desiredVelocity.magnitude);
        }

        public void OnExit() {
            HUDManager.Instance.CloseContextMenu();
            HUDManager.Instance.CloseInventory();

            // Fermeture du radial (clic ailleurs, abandon) : libère la session NPC active SAUF si
            // une modale d'interaction NPC est déjà ouverte (TALK a déclenché DialogueUI, ou
            // OpenShop a déclenché MerchantShopUI) — auquel cas la session est portée par la modale
            // et sera libérée à sa fermeture.
            if (NpcInteractionSession.ActiveNpcId.HasValue) {
                bool dialogueOpen = Sim.UI.DialogueUI.Instance != null
                                    && Sim.UI.DialogueUI.Instance.gameObject.activeSelf;
                bool shopOpen     = Sim.UI.MerchantShopUI.Instance != null
                                    && Sim.UI.MerchantShopUI.Instance.gameObject.activeSelf;
                if (!dialogueOpen && !shopOpen) {
                    NpcInteractionSession.End(NpcInteractionSession.ActiveNpcId.Value);
                }
            }
        }
    }
}