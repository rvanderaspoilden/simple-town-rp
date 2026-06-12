using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.Missions {
    /// <summary>
    /// Panel HUD du board. Singleton scene-scoped — un seul board ouvert à la
    /// fois. Doit être configuré en éditeur :
    /// - Root GO inactif au démarrage
    /// - Title TMP_Text
    /// - Entry container (RectTransform du Content du ScrollView vertical)
    /// - Entry prefab (MissionBoardEntryUI)
    /// - Close button
    ///
    /// Souscrit à MissionBoardClient.BoardUpdated pour redessiner sur push serveur.
    /// </summary>
    public class MissionBoardUI : MonoBehaviour {
        public static MissionBoardUI Instance { get; private set; }

        [SerializeField] private GameObject root;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private RectTransform entryContainer;
        [SerializeField] private MissionBoardEntryUI entryPrefab;
        [SerializeField] private Button closeButton;

        private MissionBoard _currentBoard;
        private readonly List<MissionBoardEntryUI> _spawned = new List<MissionBoardEntryUI>();

        public MissionBoard CurrentBoard => _currentBoard;
        public bool IsOpen => root != null && root.activeSelf;

        private void Awake() {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (root != null) root.SetActive(false);
            if (closeButton != null) closeButton.onClick.AddListener(Close);
        }

        private void OnEnable() {
            MissionBoardClient.Instance.BoardUpdated += OnBoardUpdated;
            MissionClientManager.Instance.MissionOffered  += OnMissionStateChanged;
            MissionClientManager.Instance.MissionFinished += OnMissionStateChanged;
        }

        private void OnDisable() {
            MissionBoardClient.Instance.BoardUpdated -= OnBoardUpdated;
            MissionClientManager.Instance.MissionOffered  -= OnMissionStateChanged;
            MissionClientManager.Instance.MissionFinished -= OnMissionStateChanged;
        }

        public void Open(MissionBoard board) {
            if (board == null) return;
            if (_currentBoard != null && _currentBoard != board) {
                MissionBoardClient.Instance.RequestClose(_currentBoard.ProfessionId);
            }
            _currentBoard = board;
            if (titleText != null) titleText.text = board.BoardTitle;
            if (root != null) root.SetActive(true);

            MissionBoardClient.Instance.RequestOpen(board.ProfessionId);
            Render(MissionBoardClient.Instance.GetEntries(board.ProfessionId));
        }

        public void Close() {
            if (_currentBoard == null) return;
            // NB : on ne RequestClose PAS ici. L'abonnement au snapshot reste actif pour
            // la session, ce qui alimente l'affichage physique (MissionBoardDisplay) en
            // continu — le push serveur est peu coûteux et le serveur nettoie l'abonné
            // à la déconnexion. La fermeture de la UI ne masque que le panneau HUD.
            _currentBoard = null;
            if (root != null) root.SetActive(false);
            ClearEntries();
        }

        private void OnBoardUpdated(string professionId, MissionBoardEntry[] entries) {
            if (_currentBoard == null || _currentBoard.ProfessionId != professionId) return;
            Render(entries);
        }

        private void OnMissionStateChanged(MissionClientState _) {
            if (!IsOpen || _currentBoard == null) return;
            Render(MissionBoardClient.Instance.GetEntries(_currentBoard.ProfessionId));
        }

        private void Render(MissionBoardEntry[] entries) {
            ClearEntries();
            if (entries == null) return;
            for (int i = 0; i < entries.Length; i++) {
                var ui = Instantiate(entryPrefab, entryContainer);
                ui.Bind(entries[i], OnTakeRequested);
                _spawned.Add(ui);
            }
        }

        private void OnTakeRequested(string instanceId) {
            MissionBoardClient.Instance.RequestTake(instanceId);
        }

        private void ClearEntries() {
            for (int i = 0; i < _spawned.Count; i++) {
                if (_spawned[i] != null) Destroy(_spawned[i].gameObject);
            }
            _spawned.Clear();
        }
    }
}
