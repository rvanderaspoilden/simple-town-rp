using System.Collections.Generic;
using Mirror;
using Sim.Enums;
using UnityEngine;
using Action = Sim.Interactables.Action;

namespace Sim.Jobs {
    /// <summary>
    /// Bac de tri posé dans la scène. Chaque bac accepte une seule catégorie
    /// d'item et expose une action USE (« Déposer ») qui résout le colis tenu
    /// contre la catégorie attendue côté serveur.
    /// </summary>
    public class SortingBin : CareerInteractableBase {
        [Header("Sorting bin")]
        [Tooltip("Identifiant stable du bac. Doit être unique dans la scène.")]
        [SerializeField] private string binId;

        [Tooltip("Catégorie d'item acceptée par ce bac.")]
        [SerializeField] private SortingCategory acceptedCategory;

        [Header("Couleur de catégorie")]
        [Tooltip("Couleur représentant la catégorie acceptée. Appliquée au matériau du bac " +
                 "à l'instanciation et reprise par l'étiquette des colis de cette catégorie : " +
                 "le joueur n'a qu'à déposer le colis de la même couleur dans ce bac.")]
        [SerializeField] private Color categoryColor = Color.white;

        [Tooltip("Renderer du bac à teinter. Vide = premier Renderer trouvé dans les enfants.")]
        [SerializeField] private Renderer targetRenderer;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private static readonly Dictionary<string, SortingBin> _all =
            new Dictionary<string, SortingBin>();

        public static IEnumerable<SortingBin> All => _all.Values;

        public string BinId => binId;
        public SortingCategory AcceptedCategory => acceptedCategory;
        public Color CategoryColor => categoryColor;
        public override MissionHighlightKind HighlightKind => MissionHighlightKind.SortingBin;
        // Cible de DÉPÔT : surligné uniquement quand le joueur tient un colis à trier.
        public override MissionHighlightPhase HighlightPhase => MissionHighlightPhase.Holding;
        protected override string GetHighlightId() => binId;

        protected override void Awake() {
            base.Awake();
            if (string.IsNullOrEmpty(binId)) {
                Debug.LogError($"[SortingBin] '{name}' has empty binId — fix it in the Inspector.");
                return;
            }
            if (_all.ContainsKey(binId))
                Debug.LogWarning($"[SortingBin] duplicate binId '{binId}' on '{name}'.");
            _all[binId] = this;

            ApplyCategoryColor();
        }

        /// <summary>
        /// Teinte le matériau du bac avec <see cref="categoryColor"/>. Crée une instance
        /// de matériau propre à ce renderer (pas de partage entre bacs).
        /// </summary>
        private void ApplyCategoryColor() {
            var rend = targetRenderer != null ? targetRenderer : GetComponentInChildren<Renderer>();
            if (rend == null) return;

            var mat = rend.material; // instance per-renderer
            if (mat.HasProperty(BaseColorId)) mat.SetColor(BaseColorId, categoryColor);
            else mat.color = categoryColor;
        }

        /// <summary>
        /// Couleur associée à une catégorie, lue depuis le bac de la scène qui l'accepte.
        /// Sert au colis pour teinter son étiquette de la même couleur que le bon bac.
        /// </summary>
        public static bool TryGetCategoryColor(SortingCategory category, out Color color) {
            foreach (var bin in _all.Values) {
                if (bin != null && bin.acceptedCategory == category) {
                    color = bin.categoryColor;
                    return true;
                }
            }
            color = Color.white;
            return false;
        }

        protected override void OnDestroy() {
            base.OnDestroy();
            if (!string.IsNullOrEmpty(binId)) _all.Remove(binId);
        }

        public static SortingBin Get(string id) {
            if (string.IsNullOrEmpty(id)) return null;
            _all.TryGetValue(id, out var bin);
            return bin;
        }

        protected override void HandleAction(Action action) {
            if (action.Type != ActionTypeEnum.USE && action.Type != ActionTypeEnum.OPEN) return;
            if (!NetworkClient.isConnected) return;

            if (!HasActiveSortItemsStep()) {
                if (NotificationManager.Instance != null) {
                    NotificationManager.Instance.AddNotification(
                        "Aucune mission ne te demande de trier des colis.",
                        NotificationType.JOB);
                }
                return;
            }

            NetworkClient.Send(new JobSortDepositMessage { binId = binId ?? string.Empty });
        }

        /// <summary>
        /// Le joueur a-t-il une mission active dont le step courant est un
        /// SortItemsStepDefinition ? Évite d'envoyer un message au serveur si
        /// aucune mission ne consomme un dépôt.
        /// </summary>
        private static bool HasActiveSortItemsStep() {
            var states = JobClientManager.Instance?.States;
            if (states == null) return false;
            foreach (var state in states.Values) {
                if (state.Status != JobStatus.Active) continue;
                var def = state.Definition;
                if (def == null) continue;
                int idx = state.CurrentStepIndex;
                if (idx < 0 || idx >= def.Steps.Count) continue;
                if (def.Steps[idx] is SortItemsStepDefinition) return true;
            }
            return false;
        }
    }
}
