using System;
using System.Collections.Generic;
using System.Linq;
using Interaction;
using Sim.Enums;
using UnityEngine;
using Action = Sim.Interactables.Action;

namespace Sim.Jobs {
    /// <summary>
    /// Shared base for scene objects that only respond to interactions from a
    /// player whose current career matches `requiredJob` (PackagingMachine for
    /// Livreur, trash bin for Cleaner, …). Handles the IInteractable
    /// boilerplate + the client-side career gate (toast + short-circuit).
    /// Subclasses implement <see cref="HandleAction"/> with what to actually
    /// do once the gate passes (typically a NetworkClient.Send(...)).
    /// </summary>
    public abstract class CareerInteractableBase : MonoBehaviour, IInteractable {
        [Header("Career gate")]
        [Tooltip("Career the player must be employed in to use this object.")]
        [SerializeField] protected JobCategory requiredJob = JobCategory.Delivery;

        [Tooltip("Toast shown when the local player's career doesn't match. Use {job} as a placeholder for the human-readable label.")]
        [SerializeField] private string wrongJobMessage = "Réservé aux {job}.";

        [Header("Interaction")]
        [Tooltip("Drag the Action SO templates (USE, OPEN, …) that the radial menu should expose.")]
        [SerializeField] protected List<Action> actionTemplates = new List<Action>();

        [SerializeField] protected float interactionRange = 3f;

        protected Action[] _actions = Array.Empty<Action>();

        public JobCategory RequiredJob => requiredJob;

        /// <summary>
        /// Subclass hook : type de highlight de mission de ce prop (PackagingMachine,
        /// SortingBin, …). None = pas de highlight. L'id précis utilisé pour la voie
        /// "ciblage exact" vient de <see cref="GetHighlightId"/>.
        /// </summary>
        public virtual MissionHighlightKind HighlightKind => MissionHighlightKind.None;

        /// <summary>Id stable du prop (machineId, binId) réutilisé pour la voie de ciblage précis.</summary>
        protected virtual string GetHighlightId() => null;

        private MissionHighlightEffect _highlightEffect;

        protected virtual void Awake() {
            _actions = actionTemplates.Where(a => a != null).Select(Instantiate).ToArray();
            foreach (var a in _actions) a.OnExecute += OnActionExecutedInternal;
        }

        protected virtual void Start() {
            if (HighlightKind != MissionHighlightKind.None) {
                _highlightEffect = GetComponent<MissionHighlightEffect>();
                if (_highlightEffect == null) _highlightEffect = gameObject.AddComponent<MissionHighlightEffect>();
                MissionHighlightManager.Register(HighlightKind, GetHighlightId(), _highlightEffect, requiredJob);
            }
        }

        protected virtual void OnDestroy() {
            if (_highlightEffect != null) MissionHighlightManager.Unregister(_highlightEffect);

            foreach (var a in _actions) {
                if (a != null) a.OnExecute -= OnActionExecutedInternal;
            }
        }

        private void OnActionExecutedInternal(Action action) {
            if (!IsLocalPlayerEmployedHere()) {
                NotifyWrongJob();
                return;
            }
            HandleAction(action);
        }

        private bool IsLocalPlayerEmployedHere() {
            var local = Sim.PlayerController.Local;
            if (local == null || local.CharacterData == null) return false;
            return local.CharacterData.CurrentJobCategory == requiredJob;
        }

        private void NotifyWrongJob() {
            if (NotificationManager.Instance == null) return;
            var label = JobCategoryLabels.Display(requiredJob);
            var text = string.IsNullOrEmpty(wrongJobMessage)
                ? $"Réservé aux {label}."
                : wrongJobMessage.Replace("{job}", label);
            NotificationManager.Instance.AddNotification(text, NotificationType.JOB);
        }

        /// <summary>Subclass hook. Career gate already passed when this runs.</summary>
        protected abstract void HandleAction(Action action);

        // ── IInteractable ────────────────────────────────────────────────
        public float GetRange() => interactionRange;
        public bool IsInteractable() => true;
        public bool IsRightClickOnly() => false;
        public Action[] GetActions(bool withPriority = false) => _actions;
        public virtual void StopInteraction() { }
    }
}
