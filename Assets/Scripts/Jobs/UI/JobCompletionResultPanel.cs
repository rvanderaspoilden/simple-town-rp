using UnityEngine;
using UnityEngine.UI;

namespace Sim.Jobs {
    /// <summary>
    /// Modale unique affichée à la fin de toute mission complétée.
    /// Le rendu réel est délégué à une JobResultView fille, sélectionnée
    /// par variant (transmis dans JobFinishedMessage). Chaque variant a
    /// son propre GameObject avec sa propre mise en page.
    ///
    /// Ce GameObject DOIT rester actif. `root` est un enfant que l'on
    /// active/désactive.
    /// </summary>
    public class JobCompletionResultPanel : MonoBehaviour {
        public static JobCompletionResultPanel Instance { get; private set; }

        [Header("Root (enfant à masquer/afficher, PAS ce GameObject)")]
        [SerializeField] private GameObject root;

        [Header("Vues — une par JobResultVariant")]
        [Tooltip("Une JobResultView par variant. Auto-rempli au Awake si vide via GetComponentsInChildren.")]
        [SerializeField] private JobResultView[] views;

        [Tooltip("Vue à afficher quand aucune autre ne matche le variant reçu.")]
        [SerializeField] private JobResultView fallbackView;

        [Header("Buttons")]
        [SerializeField] private Button closeButton;

        private bool _subscribed;

        private void Awake() {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            if (views == null || views.Length == 0)
                views = GetComponentsInChildren<JobResultView>(true);

            if (closeButton != null) closeButton.onClick.AddListener(Close);
            Subscribe();
            Show(false);
        }

        private void OnDestroy() {
            Unsubscribe();
            if (Instance == this) Instance = null;
        }

        private void Subscribe() {
            if (_subscribed) return;
            JobClientManager.Instance.JobFinished += OnJobFinished;
            _subscribed = true;
        }

        private void Unsubscribe() {
            if (!_subscribed) return;
            JobClientManager.Instance.JobFinished -= OnJobFinished;
            _subscribed = false;
        }

        private void OnJobFinished(JobClientState state) {
            if (state.Status != JobStatus.Completed) return;

            var picked = PickView(state.CompletionVariant);
            if (views != null) {
                foreach (var v in views) {
                    if (v != null) v.gameObject.SetActive(v == picked);
                }
            }
            if (picked != null) picked.Render(state);

            Show(true);
        }

        private JobResultView PickView(JobResultVariant variant) {
            if (views != null) {
                foreach (var v in views) {
                    if (v != null && v.Variant == variant) return v;
                }
            }
            return fallbackView;
        }

        public void Close() => Show(false);

        private void Show(bool visible) {
            if (root != null) root.SetActive(visible);
        }
    }
}
