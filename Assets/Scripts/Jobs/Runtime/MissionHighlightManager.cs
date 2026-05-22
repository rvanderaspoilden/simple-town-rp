using System.Collections.Generic;
using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Singleton driving the mission highlight system. Listens to JobClientManager events
    /// to toggle highlighting on the current mission target and updates the global
    /// shader pulse variable.
    /// </summary>
    public class MissionHighlightManager : MonoBehaviour {
        public static MissionHighlightManager Instance { get; private set; }

        private static readonly Dictionary<string, MissionHighlightEffect> _registry = new Dictionary<string, MissionHighlightEffect>();

        private string _currentInstanceId;
        private string _currentTargetId;
        private MissionHighlightEffect _activeEffect;

        [Header("Pulse Animation")]
        [SerializeField] private float pulseSpeed = 3f;
        [SerializeField] private float pulseMin = 0.85f;
        [SerializeField] private float pulseMax = 1.15f;

        private static readonly int PulseId = Shader.PropertyToID("_MissionOutlinePulse");

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start() {
            Subscribe();
        }

        private void OnDestroy() {
            Unsubscribe();
        }

        public static void Register(string id, MissionHighlightEffect effect) {
            if (string.IsNullOrEmpty(id)) return;
            _registry[id] = effect;
            if (Instance != null && Instance._currentTargetId == id) {
                Instance.RefreshHighlight();
            }
        }

        public static void Unregister(string id, MissionHighlightEffect effect) {
            if (string.IsNullOrEmpty(id)) return;
            if (_registry.TryGetValue(id, out var current) && current == effect) {
                _registry.Remove(id);
                if (Instance != null && Instance._activeEffect == effect) {
                    Instance._activeEffect.Hide();
                    Instance._activeEffect = null;
                }
            }
        }

        private void Subscribe() {
            var c = JobClientManager.Instance;
            if (c == null) {
                Debug.LogWarning("[MissionHighlightManager] JobClientManager not found — highlights won't work.");
                return;
            }
            c.JobOffered      += OnJobUpdate;
            c.JobStepAdvanced += OnJobUpdate;
            c.JobFinished     += OnJobFinished;
        }

        private void Unsubscribe() {
            var c = JobClientManager.Instance;
            if (c == null) return;
            c.JobOffered      -= OnJobUpdate;
            c.JobStepAdvanced -= OnJobUpdate;
            c.JobFinished     -= OnJobFinished;
        }

        private void OnJobUpdate(JobClientState state) {
            _currentInstanceId = state.InstanceId;
            ApplyTarget(state);
        }

        private void OnJobFinished(JobClientState state) {
            if (state.InstanceId != _currentInstanceId) return;
            _currentInstanceId = null;
            _currentTargetId = null;
            RefreshHighlight();
        }

        private void ApplyTarget(JobClientState state) {
            if (state.Status != JobStatus.Active) {
                _currentTargetId = null;
            } else {
                _currentTargetId = state.CurrentTargetId;
            }
            RefreshHighlight();
        }

        private void RefreshHighlight() {
            if (_activeEffect != null) {
                _activeEffect.Hide();
                _activeEffect = null;
            }

            if (!string.IsNullOrEmpty(_currentTargetId) && _registry.TryGetValue(_currentTargetId, out var effect)) {
                _activeEffect = effect;
                _activeEffect.Show();
            }
        }

        private void Update() {
            float pulse = Mathf.Lerp(pulseMin, pulseMax, (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f);
            Shader.SetGlobalFloat(PulseId, pulse);
        }
    }
}
