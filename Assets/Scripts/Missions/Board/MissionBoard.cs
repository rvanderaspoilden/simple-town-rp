using System;
using System.Collections.Generic;
using System.Linq;
using Interaction;
using Sim.Enums;
using Sim.Professions;
using UnityEngine;
using Action = Sim.Interactables.Action;

namespace Sim.Missions {
    /// <summary>
    /// Panneau d'annonces d'une entreprise. Posé en scène sur un GameObject
    /// avec un collider, il s'expose comme IInteractable au système existant
    /// (radial menu / Action SO). À l'exécution de l'action OPEN, ouvre la
    /// MissionBoardUI scopée sur la catégorie configurée.
    ///
    /// Pas de NetworkBehaviour : c'est un déclencheur local. L'autorité
    /// reste côté MissionBoardServer pour la liste des missions.
    /// </summary>
    public class MissionBoard : MonoBehaviour, IInteractable {
        [Header("Identification")]
        [Tooltip("Métier (ProfessionConfig) affiché par ce board.")]
        [SerializeField] private ProfessionConfig profession;

        [Tooltip("Titre affiché en haut de la UI.")]
        [SerializeField] private string boardTitle = "Livraisons";

        [Header("Interaction")]
        [Tooltip("Liste d'Action SO exposées au radial menu. Drag OPEN.asset (ActionTypeEnum.OPEN) ici.")]
        [SerializeField] private List<Action> actionTemplates = new List<Action>();

        [SerializeField] private float interactionRange = 3f;

        private Action[] _actions = Array.Empty<Action>();

        public ProfessionConfig Profession => profession;
        public string ProfessionId => profession != null ? profession.id : "";
        public string BoardTitle => boardTitle;

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
                case ActionTypeEnum.OPEN:
                    Open();
                    break;
            }
        }

        public void Open() {
            // Local career pre-check: avoid sending MissionBoardOpenMessage if the
            // player isn't the right job. Server still enforces the same check
            // in MissionBoardServer.OpenBoard — this is purely for UX feedback.
            var localPlayer = Sim.PlayerController.Local;
            var playerJob = localPlayer != null && localPlayer.CharacterData != null
                ? localPlayer.CharacterData.CurrentProfessionId
                : "";
            if (playerJob != ProfessionId) {
                NotificationManager.Instance?.AddNotification(
                    "Tu n'es pas employé pour ce métier.", PhoneAppIds.Career);
                return;
            }

            var ui = MissionBoardUI.Instance;
            if (ui == null) {
                Debug.LogWarning("[MissionBoard] MissionBoardUI.Instance is null — UI not initialized in scene.");
                return;
            }
            ui.Open(this);
        }

        public void Close() {
            var ui = MissionBoardUI.Instance;
            if (ui != null && ui.CurrentBoard == this) ui.Close();
        }

        // ── IInteractable ────────────────────────────────────────────────
        public float GetRange() => interactionRange;
        public bool IsInteractable() => true;
        public bool IsRightClickOnly() => false;
        public Action[] GetActions(bool withPriority = false) => _actions;
        public void StopInteraction() { /* UI fermable manuellement par le bouton Close */ }
    }
}
