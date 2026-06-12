using System.Collections.Generic;
using Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.Missions {
    /// <summary>
    /// HUD persistant de la mission active du joueur local. Reste visible
    /// tant qu'une mission est Active ou Offered ; se masque sinon.
    ///
    /// Affichage simplifié en todo-list : titre + icône + liste des steps avec
    /// case à cocher (cochée = step complété). Plus de barre de progression,
    /// plus de timer, plus de distance — la complétion par step suffit.
    ///
    /// IMPORTANT : le GameObject qui porte ce script DOIT rester actif au
    /// démarrage. Le champ `root` référence un ENFANT visuel qui sera
    /// activé/désactivé. Si tu désactives le GO porteur, Awake ne s'exécute
    /// jamais et le HUD ne s'abonnera jamais aux events.
    /// </summary>
    public class MissionActiveHUD : MonoBehaviour {
        public static MissionActiveHUD Instance { get; private set; }

        [Header("Root (enfant à masquer/afficher, PAS ce GameObject)")]
        [SerializeField] private GameObject root;

        [Header("Main Content")]
        [SerializeField] private Image iconImage;
        [SerializeField] private TMP_Text titleText;
        [Tooltip("Optionnel : affichage mono-étape, utilisé uniquement si la todo-list n'est pas câblée.")]
        [SerializeField] private TMP_Text stepText;

        [Header("Todo-list des steps")]
        [Tooltip("Conteneur (avec VerticalLayoutGroup) où sont instanciées les lignes de steps.")]
        [SerializeField] private Transform stepsContainer;
        [Tooltip("Template d'une ligne de step (gardé INACTIF) ; cloné une fois par step.")]
        [SerializeField] private MissionStepRowUI stepRowTemplate;

        [Header("Buttons")]
        [SerializeField] private Button acceptButton;
        [SerializeField] private Button abandonButton;

        private string _currentInstanceId;
        private bool _subscribed;
        private readonly List<MissionStepRowUI> _stepRows = new List<MissionStepRowUI>();

        private void Awake() {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            if (root == this.gameObject) {
                Debug.LogError("[MissionActiveHUD] 'root' must NOT point to this GameObject — " +
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
            Render(state);
            Show(true);
        }

        private void OnMissionStepAdvanced(MissionClientState state) {
            if (state.InstanceId != _currentInstanceId) return;
            Render(state);
        }

        private void OnMissionFinished(MissionClientState state) {
            if (state.InstanceId != _currentInstanceId) return;
            _currentInstanceId = null;
            Show(false);
        }

        private void Render(MissionClientState state) {
            if (titleText != null) {
                titleText.text = state.Definition != null
                    ? state.Definition.DisplayNameKey
                    : state.InstanceId;
            }

            bool useTodoList = stepsContainer != null && stepRowTemplate != null;

            // Ancien affichage mono-étape : conservé en repli si la todo-list n'est pas câblée.
            if (stepText != null) {
                stepText.gameObject.SetActive(!useTodoList);
                if (!useTodoList) {
                    stepText.text = string.IsNullOrEmpty(state.CurrentPromptKey)
                        ? $"Étape {state.CurrentStepIndex + 1}"
                        : state.CurrentPromptKey;
                }
            }

            if (useTodoList) RebuildSteps(state);

            if (iconImage != null && state.Definition != null) {
                iconImage.sprite = state.Definition.Icon;
                iconImage.gameObject.SetActive(iconImage.sprite != null);
            }

            bool offered = state.Status == MissionStatus.Offered;
            if (acceptButton != null) acceptButton.gameObject.SetActive(offered);
            if (abandonButton != null) abandonButton.gameObject.SetActive(!offered);
        }

        /// <summary>
        /// (Re)construit la todo-list des steps. Un step est COCHÉ s'il est complété
        /// (index &lt; étape courante quand la mission est Active), mis en avant s'il est
        /// l'étape courante, sinon « à faire ». En statut Offered (offre non acceptée),
        /// aucun step n'est coché.
        /// </summary>
        private void RebuildSteps(MissionClientState state) {
            var steps = state.Definition != null ? state.Definition.Steps : null;
            int count = steps != null ? steps.Count : 0;

            // Pool : on instancie autant de lignes que nécessaire, on réutilise ensuite.
            while (_stepRows.Count < count) {
                var row = Instantiate(stepRowTemplate, stepsContainer);
                _stepRows.Add(row);
            }

            bool active = state.Status == MissionStatus.Active;
            for (int i = 0; i < _stepRows.Count; i++) {
                if (i >= count) {
                    _stepRows[i].gameObject.SetActive(false);
                    continue;
                }

                var step = steps[i];
                string text = (step != null && !string.IsNullOrEmpty(step.PromptKey))
                    ? step.PromptKey
                    : $"Étape {i + 1}";

                bool done = active && i < state.CurrentStepIndex;
                bool current = active && i == state.CurrentStepIndex;

                _stepRows[i].gameObject.SetActive(true);
                _stepRows[i].Set(text, done, current);
            }

            // Le template reste toujours masqué.
            stepRowTemplate.gameObject.SetActive(false);
        }

        private void Show(bool visible) {
            if (root != null) root.SetActive(visible);
        }

        private void OnAcceptClicked() {
            if (string.IsNullOrEmpty(_currentInstanceId)) return;
            MissionClientManager.Instance.SendAccept(_currentInstanceId);
        }

        private void OnAbandonClicked() {
            if (string.IsNullOrEmpty(_currentInstanceId)) return;
            MissionClientManager.Instance.SendAbandon(_currentInstanceId);
        }
    }
}
