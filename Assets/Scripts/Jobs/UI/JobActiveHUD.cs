using Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.Jobs {
    /// <summary>
    /// HUD persistant de la mission active du joueur local. Reste visible
    /// tant qu'une mission est Active ou Offered ; se masque sinon.
    ///
    /// IMPORTANT : le GameObject qui porte ce script DOIT rester actif au
    /// démarrage. Le champ `root` référence un ENFANT visuel qui sera
    /// activé/désactivé. Si tu désactives le GO porteur, Awake ne s'exécute
    /// jamais et le HUD ne s'abonnera jamais aux events.
    /// </summary>
    public class JobActiveHUD : MonoBehaviour {
        public static JobActiveHUD Instance { get; private set; }

        [Header("Root (enfant à masquer/afficher, PAS ce GameObject)")]
        [SerializeField] private GameObject root;

        [Header("Main Content")]
        [SerializeField] private Image iconImage;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text stepText;

        [Header("Dynamic Progress Group")]
        [Tooltip("The group containing the slider and/or text.")]
        [SerializeField] private GameObject progressGroup;
        [SerializeField] private Slider progressBar;
        [SerializeField] private TMP_Text progressText;

        [Header("Buttons")]
        [SerializeField] private Button acceptButton;
        [SerializeField] private Button abandonButton;

        [Header("Refresh")]
        [Tooltip("Intervalle de recalcul de la distance NavMesh (secondes).")]
        [SerializeField] private float distanceRefreshInterval = 0.25f;

        private enum ProgressMode { None, Distance, Timer, Counter }
        private ProgressMode _currentMode = ProgressMode.None;

        private string _currentInstanceId;
        private string _currentTargetId;
        private bool _subscribed;
        private float _nextDistanceUpdate;
        private JobClientState _currentState;

        private void Awake() {
if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            if (root == this.gameObject) {
                Debug.LogError("[JobActiveHUD] 'root' must NOT point to this GameObject — " +
                               "assign a CHILD panel instead, or the HUD will never initialize.");
            }

            if (acceptButton != null) acceptButton.onClick.AddListener(OnAcceptClicked);
            if (abandonButton != null) abandonButton.onClick.AddListener(OnAbandonClicked);

            Subscribe();
            Show(false);
        }

        private void OnDestroy() {
            Unsubscribe();
            if (Instance == this) Instance = null;
        }

        private void Subscribe() {
            if (_subscribed) return;
            var c = JobClientManager.Instance;
            c.JobOffered      += OnJobOffered;
            c.JobStepAdvanced += OnJobStepAdvanced;
            c.JobFinished     += OnJobFinished;
            c.SortProgress    += OnSortProgress;
            _subscribed = true;
        }

        private void Unsubscribe() {
            if (!_subscribed) return;
            var c = JobClientManager.Instance;
            c.JobOffered      -= OnJobOffered;
            c.JobStepAdvanced -= OnJobStepAdvanced;
            c.JobFinished     -= OnJobFinished;
            c.SortProgress    -= OnSortProgress;
            _subscribed = false;
        }

        private void OnSortProgress(JobClientState state, JobSortProgressMessage msg) {
            if (msg.instanceId != _currentInstanceId) return;

            if (msg.finished) {
                _currentMode = ProgressMode.None;
                UpdateProgressDisplay();
                return;
            }

            _currentMode = ProgressMode.Counter;
            UpdateProgressDisplay();

            if (progressBar != null) {
                progressBar.maxValue = msg.totalCount;
                progressBar.value = msg.resolvedCount;
            }

            if (progressText != null)
                progressText.text = $"{msg.resolvedCount} / {msg.totalCount}";
        }

        private void OnJobOffered(JobClientState state) {
            _currentInstanceId = state.InstanceId;
            _currentTargetId = state.CurrentTargetId;
            _currentState = state;
            Render(state);
            Show(true);
        }

        private void OnJobStepAdvanced(JobClientState state) {
            if (state.InstanceId != _currentInstanceId) return;
            _currentTargetId = state.CurrentTargetId;
            _currentState = state;
            Render(state);
        }

        private void OnJobFinished(JobClientState state) {
            if (state.InstanceId != _currentInstanceId) return;
            _currentInstanceId = null;
            _currentTargetId = null;
            _currentState = null;
            Show(false);
        }

        private void Render(JobClientState state) {
            if (titleText != null) {
                titleText.text = state.Definition != null
                    ? state.Definition.DisplayNameKey
                    : state.InstanceId;
            }

            if (stepText != null) {
                stepText.text = string.IsNullOrEmpty(state.CurrentPromptKey)
                    ? $"Étape {state.CurrentStepIndex + 1}"
                    : state.CurrentPromptKey;
            }

            if (iconImage != null && state.Definition != null) {
                iconImage.sprite = state.Definition.Icon;
                iconImage.gameObject.SetActive(iconImage.sprite != null);
            }

            bool offered = state.Status == JobStatus.Offered;
            if (acceptButton != null) acceptButton.gameObject.SetActive(offered);
            if (abandonButton != null) abandonButton.gameObject.SetActive(!offered);

            // Determine mode
            // Le mode Distance (« X m ») n'est affiché que pour les steps de
            // navigation (Reach/Deliver), comme le beacon et le ruban GPS.
            if (state.Definition != null && state.Definition.ExpirationSeconds > 0) {
                _currentMode = ProgressMode.Timer;
            } else if (state.Status == JobStatus.Active && state.ShowTargetBeacon
                       && !string.IsNullOrEmpty(_currentTargetId)) {
                _currentMode = ProgressMode.Distance;
            } else {
                _currentMode = ProgressMode.None;
            }

            UpdateProgressDisplay();
        }

        private void UpdateProgressDisplay() {
            if (progressGroup != null) progressGroup.SetActive(_currentMode != ProgressMode.None);
            if (progressBar != null) progressBar.gameObject.SetActive(_currentMode == ProgressMode.Timer || _currentMode == ProgressMode.Counter);
        }

        private void Show(bool visible) {
            if (root != null) root.SetActive(visible);
        }

        private void Update() {
            if (root != null && !root.activeSelf) return;

            if (_currentMode == ProgressMode.Timer) {
                RefreshRemainingTime();
            } else if (_currentMode == ProgressMode.Distance) {
                if (Time.unscaledTime >= _nextDistanceUpdate) {
                    _nextDistanceUpdate = Time.unscaledTime + distanceRefreshInterval;
                    RefreshDistance();
                }
            }
        }

        private void RefreshRemainingTime() {
            if (_currentState == null || _currentState.Status != JobStatus.Active || _currentState.Definition == null) {
                return;
            }
            float expiration = _currentState.Definition.ExpirationSeconds;
            if (expiration <= 0f) return;

            float localElapsed = Time.unscaledTime - _currentState.SyncedAtUnscaled;
            float remaining = Mathf.Max(0f, expiration - (_currentState.ElapsedSecondsAtSync + localElapsed));
            
            if (progressBar != null) {
                progressBar.maxValue = expiration;
                progressBar.value = remaining;
            }

            if (progressText != null) {
                int total = Mathf.CeilToInt(remaining);
                int minutes = total / 60;
                int seconds = total % 60;
                progressText.text = $"{minutes:00}:{seconds:00}";
            }
        }

        private void RefreshDistance() {
            if (string.IsNullOrEmpty(_currentTargetId) || PlayerController.Local == null) {
                if (progressText != null) progressText.text = string.Empty;
                return;
            }
            if (!JobPoint.ByPointId.TryGetValue(_currentTargetId, out var point) || point == null) {
                if (progressText != null) progressText.text = string.Empty;
                return;
            }
            float d = JobDistanceUtil.Compute(PlayerController.Local.transform.position, point.transform.position);
            if (progressText != null) progressText.text = JobDistanceUtil.FormatMeters(d);
        }

        private void OnAcceptClicked() {
            if (string.IsNullOrEmpty(_currentInstanceId)) return;
            JobClientManager.Instance.SendAccept(_currentInstanceId);
        }

        private void OnAbandonClicked() {
            if (string.IsNullOrEmpty(_currentInstanceId)) return;
            JobClientManager.Instance.SendAbandon(_currentInstanceId);
        }
    }
}
