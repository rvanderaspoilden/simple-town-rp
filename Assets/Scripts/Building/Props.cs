using System.Linq;
using Interaction;
using Sim.Enums;
using Sim.Scriptables;
using UnityEngine;
using Action = Sim.Interactables.Action;

namespace Sim.Building {
    /// <summary>
    /// Legacy base for prop scripts. No longer a NetworkBehaviour — the new prop system
    /// (PropBehaviourBase + ServerPropManager + ClientPropManager) handles all networking.
    /// Props.cs is kept as a thin compatibility layer:
    ///   - exposes PropsConfig + IInteractable for legacy code paths,
    ///   - publishes the OnMoveRequest static event consumed by PlayerInteraction,
    ///   - skips its own action setup when a PropBehaviourBase is also present.
    /// </summary>
    [RequireComponent(typeof(PropsRenderer))]
    public class Props : MonoBehaviour, IInteractable {
        [Header("Props settings")]
        [SerializeField] protected PropsConfig configuration;

        [SerializeField] protected int defaultPresetId = -1;

        protected Action[] actions;
        protected Action[] unbuiltActions;
        protected PropsRenderer propsRenderer;
        protected AudioSource audioSource;

        private bool built = true;
        private int presetId = -1;
        private ApartmentController apartmentController;
        private bool _hasPropBehaviourBase;

        public delegate void PropsAction(Props props);

        public static event PropsAction OnMoveRequest;

        public static void RaiseMoveRequest(Props props) => OnMoveRequest?.Invoke(props);

        protected virtual void Awake() {
            propsRenderer = GetComponent<PropsRenderer>();
            audioSource = GetComponent<AudioSource>();
            _hasPropBehaviourBase = GetComponent<PropBehaviourBase>() != null;
        }

        protected virtual void Start() {
            apartmentController = GetComponentInParent<ApartmentController>();
            if (defaultPresetId != -1) PresetId = defaultPresetId;
            if (!_hasPropBehaviourBase) ConfigureActions();
        }

        protected virtual void OnDestroy() {
            if (!_hasPropBehaviourBase) {
                UnSubscribeActions(actions);
                UnSubscribeActions(unbuiltActions);
            }
        }

        public virtual void StopInteraction() { }

        public void InitBuilt(bool isBuilt) {
            built = isBuilt;
        }

        public ApartmentController ApartmentController {
            get => apartmentController;
            set => apartmentController = value;
        }

        private void SetupActions() {
            actions = configuration.GetActions().Where(x => x).Select(Instantiate).ToArray();
            SubscribeActions(actions);
        }

        private void SetupUnbuiltActions() {
            unbuiltActions = configuration.GetUnbuiltActions().Where(x => x).Select(Instantiate).ToArray();
            SubscribeActions(unbuiltActions);
        }

        private void SubscribeActions(Action[] actionList) {
            foreach (var action in actionList) action.OnExecute += DoAction;
        }

        private void UnSubscribeActions(Action[] actionList) {
            if (actionList == null) return;
            foreach (var action in actionList) action.OnExecute -= DoAction;
        }

        public void ConfigureActions() {
            SetupActions();
            SetupUnbuiltActions();
        }

        public float GetRange() => configuration.GetRangeToInteract();

        public bool IsInteractable() {
            if (_hasPropBehaviourBase) return false;
            Action[] acts = IsBuilt() ? actions : unbuiltActions;
            return acts != null && acts.Length > 0;
        }

        public virtual Action[] GetActions(bool withPriority = false) {
            if (_hasPropBehaviourBase) return System.Array.Empty<Action>();

            Action[] actionsToReturn = IsBuilt() ? actions : unbuiltActions;
            bool hasPermission = apartmentController != null
                              && apartmentController.IsTenant(PlayerController.Local?.CharacterData);

            actionsToReturn = actionsToReturn
                .Where(x => (x.NeedPermission && hasPermission) || !x.NeedPermission)
                .ToArray();

            if (withPriority) {
                actionsToReturn = actionsToReturn
                    .Where(x => x.Type != ActionTypeEnum.SELL && x.Type != ActionTypeEnum.MOVE)
                    .ToArray();
            }

            return actionsToReturn;
        }

        public bool IsBuilt() => built;

        public int PresetId {
            get => presetId;
            set {
                presetId = value;
                UpdatePresetRender();
            }
        }

        public void SetPresetId(int newId) {
            presetId = newId;
            UpdatePresetRender();
        }

        private void UpdatePresetRender() {
            if (configuration.Presets == null || (configuration.Presets.Length == 0 && presetId != -1)) return;
            PropsPreset preset = configuration.Presets.FirstOrDefault(x => x.ID == presetId);
            if (preset != null) propsRenderer.SetPreset(preset);
            else Debug.LogError($"Props configuration of {configuration.name} doesn't have preset with ID {presetId}");
        }

        public void SetIsBuilt(bool newValue) {
            built = newValue;
            if (propsRenderer == null) propsRenderer = GetComponent<PropsRenderer>();
            propsRenderer.UpdateGraphics();
        }

        private void DoAction(Action action) {
            switch (action.Type) {
                case ActionTypeEnum.MOVE:
                    OnMoveRequest?.Invoke(this);
                    break;
                case ActionTypeEnum.SELL:
                    // PlayerController.Local?.Sell(this);
                    break;
                case ActionTypeEnum.LOOK:
                    PlayerController.Local?.Look(transform);
                    break;
                case ActionTypeEnum.BUILD:
                    Debug.LogWarning($"[Props] Build on legacy Props without PropBehaviourBase ({name})");
                    break;
                default:
                    Execute(action);
                    break;
            }
        }

        protected virtual void Execute(Action action) { }

        public PropsConfig GetConfiguration() => configuration;

        public void SetConfiguration(PropsConfig config) => configuration = config;

        public bool IsWallProps() => configuration.GetSurfaceToPose() == BuildSurfaceEnum.WALL;

        public bool IsGroundProps() => configuration.GetSurfaceToPose() == BuildSurfaceEnum.GROUND;
    }
}
