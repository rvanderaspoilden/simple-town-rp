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
            // La cible est déjà filtrée au niveau de la visibilité des actions
            // (GetActions / IsInteractable) : si on arrive ici, c'est une cible de mission active.
            HandleAction(action);
        }

        /// <summary>
        /// Vrai si ce prop est la cible d'un step de mission ACTIF du joueur local —
        /// c.-à-d. exactement quand il est mis en surbrillance par MissionHighlightManager
        /// (filtrage carrière + ciblage kind/id inclus). Hors d'une mission le nécessitant,
        /// l'objet n'est pas highlighté → pas interactif.
        /// </summary>
        private bool IsActiveMissionTarget() =>
            _highlightEffect != null && _highlightEffect.IsHighlighted;

        /// <summary>Subclass hook. Mission-target gate already passed when this runs.</summary>
        protected abstract void HandleAction(Action action);

        // ── IInteractable ────────────────────────────────────────────────
        // Logique uniforme avec les autres props : pas d'actions disponibles → pas
        // d'interaction. Les actions ne sont exposées que si une mission active du joueur
        // cible ce prop (il est highlighté) ; sinon GetActions est vide et IsInteractable
        // est false (ni curseur ni menu au survol), sans notification.
        public float GetRange() => interactionRange;
        public bool IsInteractable() => GetActions().Length > 0;
        public bool IsRightClickOnly() => false;
        public Action[] GetActions(bool withPriority = false) =>
            IsActiveMissionTarget() ? _actions : Array.Empty<Action>();
        public virtual void StopInteraction() { }
    }
}
