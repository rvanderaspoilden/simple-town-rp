using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.Jobs {
    /// <summary>
    /// Panel HUD du board. Singleton scene-scoped — un seul board ouvert à la
    /// fois. Doit être configuré en éditeur :
    /// - Root GO inactif au démarrage
    /// - Title TMP_Text
    /// - Entry container (RectTransform du Content du ScrollView vertical)
    /// - Entry prefab (JobBoardEntryUI)
    /// - Close button
    ///
    /// Souscrit à JobBoardClient.BoardUpdated pour redessiner sur push serveur.
    /// </summary>
    public class JobBoardUI : MonoBehaviour {
        public static JobBoardUI Instance { get; private set; }

        [SerializeField] private GameObject root;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private RectTransform entryContainer;
        [SerializeField] private JobBoardEntryUI entryPrefab;
        [SerializeField] private Button closeButton;

        private JobBoard _currentBoard;
        private readonly List<JobBoardEntryUI> _spawned = new List<JobBoardEntryUI>();

        public JobBoard CurrentBoard => _currentBoard;
        public bool IsOpen => root != null && root.activeSelf;

        private void Awake() {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (root != null) root.SetActive(false);
            if (closeButton != null) closeButton.onClick.AddListener(Close);
        }

        private void OnEnable() {
            JobBoardClient.Instance.BoardUpdated += OnBoardUpdated;
            JobClientManager.Instance.JobOffered  += OnJobStateChanged;
            JobClientManager.Instance.JobFinished += OnJobStateChanged;
        }

        private void OnDisable() {
            JobBoardClient.Instance.BoardUpdated -= OnBoardUpdated;
            JobClientManager.Instance.JobOffered  -= OnJobStateChanged;
            JobClientManager.Instance.JobFinished -= OnJobStateChanged;
        }

        public void Open(JobBoard board) {
            if (board == null) return;
            if (_currentBoard != null && _currentBoard != board) {
                JobBoardClient.Instance.RequestClose(_currentBoard.Category);
            }
            _currentBoard = board;
            if (titleText != null) titleText.text = board.BoardTitle;
            if (root != null) root.SetActive(true);

            JobBoardClient.Instance.RequestOpen(board.Category);
            Render(JobBoardClient.Instance.GetEntries(board.Category));
        }

        public void Close() {
            if (_currentBoard == null) return;
            // NB : on ne RequestClose PAS ici. L'abonnement au snapshot reste actif pour
            // la session, ce qui alimente l'affichage physique (JobBoardDisplay) en
            // continu — le push serveur est peu coûteux et le serveur nettoie l'abonné
            // à la déconnexion. La fermeture de la UI ne masque que le panneau HUD.
            _currentBoard = null;
            if (root != null) root.SetActive(false);
            ClearEntries();
        }

        private void OnBoardUpdated(JobCategory category, JobBoardEntry[] entries) {
            if (_currentBoard == null || _currentBoard.Category != category) return;
            Render(entries);
        }

        private void OnJobStateChanged(JobClientState _) {
            if (!IsOpen || _currentBoard == null) return;
            Render(JobBoardClient.Instance.GetEntries(_currentBoard.Category));
        }

        private void Render(JobBoardEntry[] entries) {
            ClearEntries();
            if (entries == null) return;
            for (int i = 0; i < entries.Length; i++) {
                var ui = Instantiate(entryPrefab, entryContainer);
                ui.Bind(entries[i], OnTakeRequested);
                _spawned.Add(ui);
            }
        }

        private void OnTakeRequested(string instanceId) {
            JobBoardClient.Instance.RequestTake(instanceId);
        }

        private void ClearEntries() {
            for (int i = 0; i < _spawned.Count; i++) {
                if (_spawned[i] != null) Destroy(_spawned[i].gameObject);
            }
            _spawned.Clear();
        }
    }
}
