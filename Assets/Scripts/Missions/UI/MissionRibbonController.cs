using UnityEngine;
using Sim.Missions;
using TheBroz.Navigation;
using Mirror;
using System.Linq;

namespace Sim.Missions.UI
{
    /// <summary>
    /// Gère l'affichage du ruban de navigation (GPS) vers l'objectif de mission.
    /// S'instancie automatiquement et suit les changements de cible de mission.
    /// </summary>
    public class MissionRibbonController : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("Prefab du ruban (doit contenir RibbonPathNavigator)")]
        public GameObject ribbonPrefab;

        private RibbonPathNavigator _ribbonInstance;
        private string _activeInstanceId;
        private string _activeTargetId;
        private bool _isSubscribed;

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
            ClearRibbon();
        }

        private void Subscribe()
        {
            if (_isSubscribed) return;
            var c = MissionClientManager.Instance;
            if (c == null) return;

            c.MissionOffered += OnMissionStateUpdated;
            c.MissionStepAdvanced += OnMissionStateUpdated;
            c.MissionFinished += OnMissionFinished;
            _isSubscribed = true;
            
            // Initial sync if a job is already active
            var activeJob = c.States.Values.FirstOrDefault(s => s.Status == MissionStatus.Active);
            if (activeJob != null) OnMissionStateUpdated(activeJob);
        }

        private void Unsubscribe()
        {
            if (!_isSubscribed) return;
            var c = MissionClientManager.Instance;
            if (c == null) return;

            c.MissionOffered -= OnMissionStateUpdated;
            c.MissionStepAdvanced -= OnMissionStateUpdated;
            c.MissionFinished -= OnMissionFinished;
            _isSubscribed = false;
        }

        private void OnMissionStateUpdated(MissionClientState state)
        {
            _activeInstanceId = state.InstanceId;

            // On ne montre le GPS que pour les missions actives, et uniquement pour
            // les steps de navigation (Reach/Deliver). Les autres steps mettent en
            // évidence leur cible concrète via le highlight monde — pas de flèches.
            if (state.Status != MissionStatus.Active || !state.ShowTargetBeacon)
            {
                ClearRibbon();
                return;
            }

            UpdateTarget(state.CurrentTargetId);
        }

        private void OnMissionFinished(MissionClientState state)
        {
            if (state.InstanceId == _activeInstanceId)
            {
                _activeInstanceId = null;
                ClearRibbon();
            }
        }

        private void UpdateTarget(string targetId)
        {
            if (string.IsNullOrEmpty(targetId))
            {
                ClearRibbon();
                return;
            }

            if (targetId == _activeTargetId && _ribbonInstance != null) return;

            _activeTargetId = targetId;

            if (MissionPoint.ByPointId.TryGetValue(targetId, out var point))
            {
                EnsureRibbon();
                if (_ribbonInstance != null)
                {
                    _ribbonInstance.target = point.Transform;
                    UpdatePlayerReference();
                }
            }
            else
            {
                ClearRibbon();
            }
        }

        private void EnsureRibbon()
        {
            if (_ribbonInstance != null) return;

            if (ribbonPrefab == null)
            {
                // Chargement automatique si non assigné
                ribbonPrefab = Resources.Load<GameObject>("Prefabs/Navigation/RibbonPath");
            }

            if (ribbonPrefab != null)
            {
                GameObject go = Instantiate(ribbonPrefab);
                go.name = "[GPS] RibbonPath";
                _ribbonInstance = go.GetComponent<RibbonPathNavigator>();
            }
            else
            {
                Debug.LogWarning("[MissionRibbonController] Ribbon prefab is missing!");
            }
        }

        private void ClearRibbon()
        {
            if (_ribbonInstance != null)
            {
                Destroy(_ribbonInstance.gameObject);
                _ribbonInstance = null;
            }
            _activeTargetId = null;
        }

        private void Update()
        {
            if (_ribbonInstance != null && (_ribbonInstance.player == null || _ribbonInstance.player != GetLocalPlayerTransform()))
            {
                UpdatePlayerReference();
            }
        }

        private void UpdatePlayerReference()
        {
            if (_ribbonInstance == null) return;
            Transform t = GetLocalPlayerTransform();
            if (t != null)
            {
                _ribbonInstance.player = t;
            }
        }

        private Transform GetLocalPlayerTransform()
        {
            if (PlayerController.Local != null) return PlayerController.Local.transform;
            return null;
        }
    }
}
