using System;
using System.Collections.Generic;
using System.Linq;
using Interaction;
using Mirror;
using Sim.Enums;
using UnityEngine;
using Action = Sim.Interactables.Action;

namespace Sim.Jobs {
    /// <summary>
    /// Machine à emballer / poste de travail à poser dans la scène. Implémente
    /// IInteractable comme JobBoard — drag l'action USE.asset dans la liste
    /// pour que le radial menu propose "Utiliser".
    ///
    /// À l'exécution de l'action côté client, envoie JobUseMachineMessage au
    /// serveur. Le serveur route vers le UseMachineStep actif du joueur
    /// (s'il y en a un) et fait spawn le colis dans les mains. Sinon, no-op.
    /// </summary>
    public class PackagingMachineBehaviour : MonoBehaviour, IInteractable {
        [Header("Identification")]
        [Tooltip("Id stable de la machine (envoyé au serveur pour logs / futurs filtres).")]
        [SerializeField] private string machineId;

        [Header("Interaction")]
        [Tooltip("Drag USE.asset (Resources/Configurations/Actions/USE.asset).")]
        [SerializeField] private List<Action> actionTemplates = new List<Action>();

        [SerializeField] private float interactionRange = 3f;

        private Action[] _actions = Array.Empty<Action>();

        public string MachineId => machineId;

        private void Awake() {
            _actions = actionTemplates.Where(a => a != null).Select(Instantiate).ToArray();
            foreach (var a in _actions) a.OnExecute += OnActionExecuted;
        }

        private void OnDestroy() {
            foreach (var a in _actions) {
                if (a != null) a.OnExecute -= OnActionExecuted;
            }
        }

        private void OnActionExecuted(Action action) {
            switch (action.Type) {
                case ActionTypeEnum.USE:
                case ActionTypeEnum.OPEN:
                    if (!NetworkClient.isConnected) return;
                    NetworkClient.Send(new JobUseMachineMessage { machineId = machineId ?? string.Empty });
                    break;
            }
        }

        // ── IInteractable ────────────────────────────────────────────────
        public float GetRange() => interactionRange;
        public bool IsInteractable() => true;
        public bool IsRightClickOnly() => false;
        public Action[] GetActions(bool withPriority = false) => _actions;
        public void StopInteraction() { }
    }
}
