using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Allume/éteint l'indicateur visuel sur le JobDeliveryPoint correspondant
    /// à la cible primaire de la mission active du joueur local. Singleton
    /// scene-scoped — drop ce composant sur un GameObject persistant de la UI.
    /// </summary>
    public class JobActiveTargetIndicator : MonoBehaviour {
        private string _currentInstanceId;
        private string _currentTargetId;
        private bool _subscribed;

        private void Awake() {
            Subscribe();
        }

        private void OnDestroy() {
            Unsubscribe();
            ClearIndicator();
        }

        private void Subscribe() {
            if (_subscribed) return;
            var c = JobClientManager.Instance;
            c.JobOffered      += OnJobOffered;
            c.JobStepAdvanced += OnJobStepAdvanced;
            c.JobFinished     += OnJobFinished;
            _subscribed = true;
        }

        private void Unsubscribe() {
            if (!_subscribed) return;
            var c = JobClientManager.Instance;
            c.JobOffered      -= OnJobOffered;
            c.JobStepAdvanced -= OnJobStepAdvanced;
            c.JobFinished     -= OnJobFinished;
            _subscribed = false;
        }

        private void OnJobOffered(JobClientState state) {
            _currentInstanceId = state.InstanceId;
            ApplyTarget(state);
        }

        private void OnJobStepAdvanced(JobClientState state) {
            if (state.InstanceId != _currentInstanceId) return;
            ApplyTarget(state);
        }

        private void OnJobFinished(JobClientState state) {
            if (state.InstanceId != _currentInstanceId) return;
            _currentInstanceId = null;
            ClearIndicator();
        }

        private void ApplyTarget(JobClientState state) {
            // Seules les missions Active déclenchent un indicateur (pas Offered).
            if (state.Status != JobStatus.Active) {
                ClearIndicator();
                return;
            }

            string newTargetId = state.CurrentTargetId;
            if (newTargetId == _currentTargetId) return;

            ClearIndicator();
            _currentTargetId = newTargetId;
            if (!string.IsNullOrEmpty(newTargetId)
                && JobDeliveryPoint.ByPointId.TryGetValue(newTargetId, out var point)) {
                point.SetIndicator(true);
            }
        }

        private void ClearIndicator() {
            if (string.IsNullOrEmpty(_currentTargetId)) return;
            if (JobDeliveryPoint.ByPointId.TryGetValue(_currentTargetId, out var point)) {
                point.SetIndicator(false);
            }
            _currentTargetId = null;
        }
    }
}
