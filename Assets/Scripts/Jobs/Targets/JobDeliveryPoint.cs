using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Point statique de livraison/cible posé dans la scène. Implémente
    /// IJobTarget : côté serveur il s'enregistre auprès de JobTargetRegistry
    /// pour que les providers puissent le choisir comme cible.
    ///
    /// Visuel : assigne un GameObject enfant dans `indicator`. Il sera
    /// activé uniquement quand le joueur local a une mission qui cible ce
    /// point (géré par JobActiveTargetIndicator côté client).
    ///
    /// IMPORTANT : `pointId` doit être STABLE entre toutes les instances
    /// (serveur ET clients) pour que la résolution par id fonctionne. Les
    /// scènes partagent les mêmes scene objects → un id par instance suffit.
    /// </summary>
    public class JobDeliveryPoint : MonoBehaviour, IJobTarget {
        [Header("Identification")]
        [Tooltip("Id stable du point. Doit être unique dans la map.")]
        [SerializeField] private string pointId;

        [Tooltip("Catégorie de mission pour laquelle ce point est éligible.")]
        [SerializeField] private JobCategory category = JobCategory.Delivery;

        [Tooltip("Nom affiché dans le HUD et le board (ex. 'Parc central', 'Boîte aux lettres n°12').")]
        [SerializeField] private string displayName;

        [Header("Visual")]
        [Tooltip("GameObject enfant à activer quand ce point est la cible de la mission du joueur local.")]
        [SerializeField] private GameObject indicator;

        public static IReadOnlyDictionary<string, JobDeliveryPoint> ByPointId => _byPointId;
        private static readonly Dictionary<string, JobDeliveryPoint> _byPointId = new Dictionary<string, JobDeliveryPoint>();

        public string PointId => pointId;
        public JobCategory Category => category;

        // ── IJobTarget ────────────────────────────────────────────────
        public string TargetId => pointId;
        public JobTargetKind Kind => JobTargetKind.Zone;
        public Transform Transform => transform;
        public bool IsAvailable => isActiveAndEnabled;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? pointId : displayName;

        private void Awake() {
            if (string.IsNullOrEmpty(pointId)) {
                Debug.LogError($"[JobDeliveryPoint] '{name}' has empty pointId — fix it in the Inspector.");
                return;
            }
            if (_byPointId.ContainsKey(pointId)) {
                Debug.LogError($"[JobDeliveryPoint] duplicate pointId '{pointId}' on '{name}'.");
                return;
            }
            _byPointId[pointId] = this;
            SetIndicator(false);
        }

        private void OnEnable() {
            if (NetworkServer.active && !string.IsNullOrEmpty(pointId)) {
                JobTargetRegistry.Instance.Register(this);
            }
        }

        private void OnDisable() {
            if (NetworkServer.active && !string.IsNullOrEmpty(pointId)) {
                JobTargetRegistry.Instance.Unregister(this);
            }
            SetIndicator(false);
        }

        private void OnDestroy() {
            if (!string.IsNullOrEmpty(pointId)) _byPointId.Remove(pointId);
        }

        public void SetIndicator(bool visible) {
            if (indicator != null && indicator != gameObject) indicator.SetActive(visible);
        }
    }
}
