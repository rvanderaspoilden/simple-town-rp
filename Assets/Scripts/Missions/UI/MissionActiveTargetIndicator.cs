using UnityEngine;

namespace Sim.Missions {
    /// <summary>
    /// Allume/éteint l'indicateur visuel sur le MissionPoint correspondant
    /// à la cible primaire de la mission active du joueur local. Singleton
    /// scene-scoped — drop ce composant sur un GameObject persistant de la UI.
    /// </summary>
    public class MissionActiveTargetIndicator : MonoBehaviour {
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
            var c = MissionClientManager.Instance;
            c.MissionOffered      += OnMissionOffered;
            c.MissionStepAdvanced += OnMissionStepAdvanced;
            c.MissionFinished     += OnMissionFinished;
            _subscribed = true;
        }

        private void Unsubscribe() {
            if (!_subscribed) return;
            var c = MissionClientManager.Instance;
            c.MissionOffered      -= OnMissionOffered;
            c.MissionStepAdvanced -= OnMissionStepAdvanced;
            c.MissionFinished     -= OnMissionFinished;
            _subscribed = false;
        }

        private void OnMissionOffered(MissionClientState state) {
            _currentInstanceId = state.InstanceId;
            ApplyTarget(state);
        }

        private void OnMissionStepAdvanced(MissionClientState state) {
            if (state.InstanceId != _currentInstanceId) return;
            ApplyTarget(state);
        }

        private void OnMissionFinished(MissionClientState state) {
            if (state.InstanceId != _currentInstanceId) return;
            _currentInstanceId = null;
            ClearIndicator();
        }

        private void ApplyTarget(MissionClientState state) {
            // Seules les missions Active déclenchent un indicateur (pas Offered),
            // et seulement pour les steps de navigation (Reach/Deliver). Les autres
            // steps (UseMachine, Pickup, Sort…) mettent en évidence leur cible
            // concrète via le système de highlight monde, pas via ce beacon.
            if (state.Status != MissionStatus.Active || !state.ShowTargetBeacon) {
                ClearIndicator();
                return;
            }

            string newTargetId = state.CurrentTargetId;
            if (newTargetId == _currentTargetId) return;

            ClearIndicator();
            _currentTargetId = newTargetId;
            if (!string.IsNullOrEmpty(newTargetId)
                && MissionPoint.ByPointId.TryGetValue(newTargetId, out var point)) {
                point.SetIndicator(true);
            }
        }

        private void ClearIndicator() {
            if (string.IsNullOrEmpty(_currentTargetId)) return;
            if (MissionPoint.ByPointId.TryGetValue(_currentTargetId, out var point)) {
                point.SetIndicator(false);
            }
            _currentTargetId = null;
        }
    }
}
