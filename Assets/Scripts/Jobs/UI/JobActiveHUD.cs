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

        [Header("Texts")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text stepText;
        [SerializeField] private TMP_Text targetText;
        [SerializeField] private TMP_Text distanceText;
        [Tooltip("Optional countdown label (mm:ss) of the remaining mission time. Hidden when the job has no expiration.")]
        [SerializeField] private TMP_Text remainingTimeText;

        [Header("Sort Progress")]
        [Tooltip("Container (parent GO) à afficher/masquer quand un SortItemsStep est actif.")]
        [SerializeField] private GameObject sortProgressRoot;
        [Tooltip("Label 'X / Y colis' mis à jour à chaque dépôt.")]
        [SerializeField] private TMP_Text sortProgressText;

        [Header("Buttons")]
        [SerializeField] private Button acceptButton;
        [SerializeField] private Button abandonButton;

        [Header("Refresh")]
        [Tooltip("Intervalle de recalcul de la distance NavMesh (secondes).")]
        [SerializeField] private float distanceRefreshInterval = 0.25f;

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
                HideSortProgress();
                return;
            }

            if (sortProgressRoot != null) sortProgressRoot.SetActive(true);

            if (sortProgressText != null)
                sortProgressText.text = $"{msg.resolvedCount} / {msg.totalCount} colis";
        }

        private void HideSortProgress() {
            if (sortProgressRoot != null) sortProgressRoot.SetActive(false);
            if (sortProgressText  != null) sortProgressText.text = string.Empty;
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

            if (targetText != null) {
                targetText.text = string.IsNullOrEmpty(state.CurrentTargetName)
                    ? "—"
                    : state.CurrentTargetName;
            }

            bool offered = state.Status == JobStatus.Offered;
            if (acceptButton != null) acceptButton.gameObject.SetActive(offered);
            if (abandonButton != null) abandonButton.gameObject.SetActive(!offered);

            HideSortProgress();
        }

        private void Show(bool visible) {
            if (root != null) root.SetActive(visible);
        }

        private void Update() {
            if (root != null && !root.activeSelf) return;

            RefreshRemainingTime();

            if (distanceText == null) return;
            if (Time.unscaledTime < _nextDistanceUpdate) return;
            _nextDistanceUpdate = Time.unscaledTime + distanceRefreshInterval;
            RefreshDistance();
        }

        private void RefreshRemainingTime() {
            if (remainingTimeText == null) return;
            if (_currentState == null || _currentState.Status != JobStatus.Active || _currentState.Definition == null) {
                remainingTimeText.text = string.Empty;
                return;
            }
            float expiration = _currentState.Definition.ExpirationSeconds;
            if (expiration <= 0f) {
                remainingTimeText.text = string.Empty;
                return;
            }
            float localElapsed = Time.unscaledTime - _currentState.SyncedAtUnscaled;
            float remaining = Mathf.Max(0f, expiration - (_currentState.ElapsedSecondsAtSync + localElapsed));
            int total = Mathf.CeilToInt(remaining);
            int minutes = total / 60;
            int seconds = total % 60;
            remainingTimeText.text = $"{minutes:00}:{seconds:00}";
        }

        private void RefreshDistance() {
            if (distanceText == null) return;
            if (string.IsNullOrEmpty(_currentTargetId) || PlayerController.Local == null) {
                distanceText.text = string.Empty;
                return;
            }
            if (!JobPoint.ByPointId.TryGetValue(_currentTargetId, out var point) || point == null) {
                distanceText.text = string.Empty;
                return;
            }
            float d = JobDistanceUtil.Compute(PlayerController.Local.transform.position, point.transform.position);
            distanceText.text = JobDistanceUtil.FormatMeters(d);
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
