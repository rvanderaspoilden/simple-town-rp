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

        private static readonly Dictionary<string, SortingBin> _all =
            new Dictionary<string, SortingBin>();

        public static IEnumerable<SortingBin> All => _all.Values;

        public string BinId => binId;
        public SortingCategory AcceptedCategory => acceptedCategory;
        public override string GetTargetId() => binId;

        protected override void Awake() {
            base.Awake();
            if (string.IsNullOrEmpty(binId)) {
                Debug.LogError($"[SortingBin] '{name}' has empty binId — fix it in the Inspector.");
                return;
            }
            if (_all.ContainsKey(binId))
                Debug.LogWarning($"[SortingBin] duplicate binId '{binId}' on '{name}'.");
            _all[binId] = this;
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
