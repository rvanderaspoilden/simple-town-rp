using System.Linq;
using Interaction;
using Sim.Enums;
using Sim.Scriptables;
using UnityEngine;
using Action = Sim.Interactables.Action;

namespace Sim.Building {
    /// <summary>
    /// Cosmetic / decorative prop that lives directly in a scene without any NetworkIdentity.
    /// Used for the City scene where most props have no runtime state to synchronize.
    /// Only purely-local actions (LOOK and subclass-specific Execute) are supported.
    /// Stateful behaviours (build/sell/move, lock state, occupancy, etc.) use PropBehaviourBase.
    /// </summary>
    [RequireComponent(typeof(PropsRenderer))]
    public class StaticProp : MonoBehaviour, IInteractable {
        [Header("Props settings")]
        [SerializeField]
        protected PropsConfig configuration;

        [SerializeField]
        protected int defaultPresetId = -1;

        protected Action[] actions;
        protected PropsRenderer propsRenderer;
        protected AudioSource audioSource;

        protected virtual void Awake() {
            this.propsRenderer = GetComponent<PropsRenderer>();
            this.audioSource = GetComponent<AudioSource>();
        }

        protected virtual void Start() {
            this.SetupActions();
            this.ApplyDefaultPreset();
        }

        protected virtual void OnDestroy() {
            this.UnSubscribeActions(this.actions);
        }

        public float GetRange() {
            return this.configuration ? this.configuration.GetRangeToInteract() : 0f;
        }

        public bool IsInteractable() {
            return this.actions != null && this.actions.Length > 0;
        }

        public bool IsRightClickOnly() =>
            this.configuration != null && this.configuration.IsRightClickOnly();

        public virtual Action[] GetActions(bool withPriority = false) {
            if (this.actions == null) return System.Array.Empty<Action>();

            // Static props have no apartment context, so any action requiring apartment
            // permission (move/sell/build/paint) is filtered out.
            Action[] result = this.actions.Where(x => !x.NeedPermission).ToArray();

            if (withPriority) {
                result = result.Where(x => x.Type != ActionTypeEnum.SELL && x.Type != ActionTypeEnum.MOVE).ToArray();
            }

            return result;
        }

        public virtual void StopInteraction() { }

        /// <summary>
        /// Override in a subclass to handle non-LOOK actions (e.g. USE on a city dispenser variant).
        /// </summary>
        protected virtual void Execute(Action action) { }

        public PropsConfig GetConfiguration() => this.configuration;

        private void SetupActions() {
            if (this.configuration == null) {
                this.actions = System.Array.Empty<Action>();
                return;
            }

            this.actions = this.configuration.GetActions().Where(x => x).Select(Instantiate).ToArray();
            this.SubscribeActions(this.actions);
        }

        private void SubscribeActions(Action[] actionList) {
            if (actionList == null) return;
            foreach (var action in actionList) {
                action.OnExecute += this.DoAction;
            }
        }

        private void UnSubscribeActions(Action[] actionList) {
            if (actionList == null) return;
            foreach (var action in actionList) {
                action.OnExecute -= this.DoAction;
            }
        }

        private void DoAction(Action action) {
            switch (action.Type) {
                case ActionTypeEnum.LOOK:
                    if (PlayerController.Local) {
                        PlayerController.Local.Look(this.transform);
                    }
                    break;

                default:
                    this.Execute(action);
                    break;
            }
        }

        private void ApplyDefaultPreset() {
            if (this.defaultPresetId == -1) return;
            if (this.configuration == null || this.configuration.Presets == null) return;

            PropsPreset preset = this.configuration.Presets.FirstOrDefault(x => x.ID == this.defaultPresetId);
            if (preset != null) {
                this.propsRenderer.SetPreset(preset);
            }
        }
    }
}
