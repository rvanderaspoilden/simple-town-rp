using System.Collections.Generic;
using Mirror;
using Sim.Professions;
using UnityEngine;

namespace Sim.Missions {
    /// <summary>
    /// Point statique cible posé dans la scène. Implémente IMissionTarget :
    /// côté serveur il s'enregistre auprès de MissionTargetRegistry pour que
    /// les providers puissent le choisir comme cible (pickup, delivery,
    /// trash, cart, …).
    ///
    /// Visuel : assigne un GameObject enfant dans `indicator`. Il sera
    /// activé uniquement quand le joueur local a une mission qui cible ce
    /// point (géré par MissionActiveTargetIndicator côté client).
    ///
    /// IMPORTANT : `pointId` doit être STABLE entre toutes les instances
    /// (serveur ET clients) pour que la résolution par id fonctionne. Les
    /// scènes partagent les mêmes scene objects → un id par instance suffit.
    /// </summary>
    public class MissionPoint : MonoBehaviour, IMissionTarget {
        [Header("Identification")]
        [Tooltip("Id stable du point. Doit être unique dans la map.")]
        [SerializeField] private string pointId;

        [Tooltip("Métier pour lequel ce point est éligible (ProfessionConfig).")]
        [SerializeField] private ProfessionConfig profession;

        [Tooltip("Rôle du point — utilisé par les providers pour le tirage (pickup vs delivery, etc.).")]
        [SerializeField] private PointRole role = PointRole.Any;

        [Tooltip("Nom affiché dans le HUD et le board (ex. 'Parc central', 'Boîte aux lettres n°12').")]
        [SerializeField] private string displayName;

        [Header("Visual")]
        [Tooltip("GameObject enfant à activer quand ce point est la cible de la mission du joueur local.")]
        [SerializeField] private GameObject indicator;

        public static IReadOnlyDictionary<string, MissionPoint> ByPointId => _byPointId;
        private static readonly Dictionary<string, MissionPoint> _byPointId = new Dictionary<string, MissionPoint>();

        public string PointId => pointId;
        public ProfessionConfig Profession => profession;
        public string ProfessionId => profession != null ? profession.id : "";
        public PointRole Role => role;

        public bool MatchesRole(PointRole required)
            => required == PointRole.Any || role == PointRole.Any || role == required;

        // ── IMissionTarget ────────────────────────────────────────────────
        public string TargetId => pointId;
        public MissionTargetKind Kind => MissionTargetKind.Zone;
        public Transform Transform => transform;
        public bool IsAvailable => isActiveAndEnabled;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? pointId : displayName;

        private void Awake() {
            if (string.IsNullOrEmpty(pointId)) {
                Debug.LogError($"[MissionPoint] '{name}' has empty pointId — fix it in the Inspector.");
                return;
            }
            if (_byPointId.ContainsKey(pointId)) {
                Debug.LogError($"[MissionPoint] duplicate pointId '{pointId}' on '{name}'.");
                return;
            }
            _byPointId[pointId] = this;
            SetIndicator(false);
        }

        private void OnEnable() {
            if (NetworkServer.active && !string.IsNullOrEmpty(pointId)) {
                MissionTargetRegistry.Instance.Register(this);
            }
        }

        private void OnDisable() {
            if (NetworkServer.active && !string.IsNullOrEmpty(pointId)) {
                MissionTargetRegistry.Instance.Unregister(this);
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
