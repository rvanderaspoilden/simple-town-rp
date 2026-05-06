using System.Linq;
using Interaction;
using Mirror;
using Sim.Building;
using Sim.Enums;
using Sim.Scriptables;
using UnityEngine;
using Action = Sim.Interactables.Action;

namespace Sim.Interactables {
    /// <summary>
    /// Legacy elevator. Stays as a standalone NetworkBehaviour during the migration:
    /// it still uses Mirror [Command] CmdUse to request a floor change.
    /// Phase 1 of the migration replaces this with TeleporterBehaviour + TeleporterPropSource.
    /// </summary>
    [RequireComponent(typeof(PropsRenderer))]
    public class Teleporter : NetworkBehaviour, IInteractable {
        [Header("Props settings")]
        [SerializeField] private PropsConfig configuration;

        [Header("Settings")]
        [SerializeField] private Transform spawnTransform;

        private HallController hallController;
        private Action[] actions;

        public delegate void UseEvent(int originFloor, int floorDestination, NetworkConnectionToClient playerConn);

        public event UseEvent OnUse;

        public Transform SpawnTransform => spawnTransform;

        public HallController HallController {
            get => hallController;
            set => hallController = value;
        }

        protected virtual void Start() {
            if (this.hallController == null) {
                this.hallController = GetComponentInParent<HallController>();
            }
            this.SetupActions();
        }

        protected virtual void OnDestroy() {
            if (this.actions == null) return;
            foreach (var action in this.actions) {
                action.OnExecute -= DoAction;
            }
        }

        // ── IInteractable ─────────────────────────────────────────────────────────

        public float GetRange() => configuration ? configuration.GetRangeToInteract() : 2f;

        public bool IsInteractable() => actions != null && actions.Length > 0;

        public Action[] GetActions(bool withPriority = false) {
            if (actions == null) return System.Array.Empty<Action>();
            if (withPriority) {
                return actions.Where(x => x.Type != ActionTypeEnum.SELL && x.Type != ActionTypeEnum.MOVE).ToArray();
            }
            return actions.ToArray();
        }

        public void StopInteraction() {
            DefaultViewUI.Instance.HideElevatorUI();
        }

        // ── Internal ──────────────────────────────────────────────────────────────

        private void SetupActions() {
            if (configuration == null) {
                actions = System.Array.Empty<Action>();
                return;
            }
            actions = configuration.GetActions().Where(x => x).Select(Instantiate).ToArray();
            foreach (var action in actions) action.OnExecute += DoAction;
        }

        private void DoAction(Action action) {
            if (action.Type == ActionTypeEnum.TELEPORT) {
                PlayerController.Local.Interact(this);
                DefaultViewUI.Instance.ShowElevatorUI(this);
            }
        }

        // ── Mirror command ────────────────────────────────────────────────────────

        [Command(requiresAuthority = false)]
        public void CmdUse(int floorDestination, NetworkConnectionToClient sender = null) {
            Debug.Log($"Server: Player {sender.identity.netId} want to go to floor {floorDestination}");
            this.hallController = GetComponentInParent<HallController>();

            int originFloor = this.hallController ? this.hallController.FloorNumber : 0;

            if (originFloor != floorDestination) {
                OnUse?.Invoke(originFloor, floorDestination, sender);
            }
        }
    }
}
