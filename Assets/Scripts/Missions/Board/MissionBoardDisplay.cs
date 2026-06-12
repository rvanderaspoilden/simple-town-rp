using System.Collections.Generic;
using Mirror;
using UnityEngine;
using Sim;
using Sim.Entities;

namespace Sim.Missions {
    /// <summary>
    /// Peuple le panneau d'affichage physique des missions. Le Canvas world-space
    /// (titre + grille) est AUTORÉ EN DUR dans la scène sous le board ; ce script ne
    /// fait QUE générer / rafraîchir les cartes dans la grille assignée
    /// (<see cref="cardsContainer"/>).
    ///
    /// Données : s'abonne proactivement au snapshot de sa catégorie via
    /// <see cref="MissionBoardClient"/> dès que le joueur local est employé dans cette
    /// catégorie (career gate serveur inchangé). Se redessine sur chaque push serveur.
    /// </summary>
    [RequireComponent(typeof(MissionBoard))]
    public class MissionBoardDisplay : MonoBehaviour {
        [Header("Références scène")]
        [Tooltip("Conteneur des cartes : le RectTransform qui porte le GridLayoutGroup, " +
                 "créé en dur dans la scène (sous le Canvas du board).")]
        [SerializeField] private RectTransform cardsContainer;

        [Header("Cartes")]
        [Tooltip("Nombre maximum de cartes affichées (au-delà, on s'arrête — board minimaliste).")]
        [SerializeField] private int maxCards = 9;
        [Tooltip("Taille de police du titre de chaque carte.")]
        [SerializeField] private int cardFontSize = 26;
        [Tooltip("Couleur d'une mission disponible.")]
        [SerializeField] private Color availableColor = new Color(0.55f, 0.85f, 0.45f);
        [Tooltip("Couleur d'une mission déjà prise (en cours).")]
        [SerializeField] private Color activeColor = new Color(0.45f, 0.65f, 0.95f);

        private MissionBoard _board;
        private readonly List<MissionBoardCardView> _cards = new List<MissionBoardCardView>();
        private bool _subscribed;

        private void Awake() {
            _board = GetComponent<MissionBoard>();
        }

        private void OnEnable() {
            MissionBoardClient.Instance.BoardUpdated += OnBoardUpdated;
            PlayerController.OnCharacterDataChanged += OnCharacterDataChanged;
            EvaluateSubscription();
            Render();
        }

        private void OnDisable() {
            MissionBoardClient.Instance.BoardUpdated -= OnBoardUpdated;
            PlayerController.OnCharacterDataChanged -= OnCharacterDataChanged;
            if (_subscribed && NetworkClient.isConnected) {
                MissionBoardClient.Instance.RequestClose(_board.ProfessionId);
            }
            _subscribed = false;
        }

        private void OnCharacterDataChanged(CharacterData _) {
            // Le job a pu changer (prise/démission) : (ré)évalue l'abonnement et redessine.
            EvaluateSubscription();
            Render();
        }

        private void OnBoardUpdated(string professionId, MissionBoardEntry[] entries) {
            if (professionId != _board.ProfessionId) return;
            Render();
        }

        private void Update() {
            // Souscription robuste : à la connexion, PlayerController.Local et
            // CharacterData ne sont pas forcément prêts au moment d'OnEnable, et le hook
            // SyncVar de CharacterData peut se déclencher AVANT que Local soit assigné.
            // On réessaie tant que l'état d'abonnement voulu n'est pas atteint (no-op une
            // fois stabilisé).
            bool want = LocalEmployedHere && NetworkClient.isConnected;
            if (want != _subscribed) {
                EvaluateSubscription();
                Render();
            }
        }

        private bool LocalEmployedHere {
            get {
                CharacterData data = PlayerController.Local != null ? PlayerController.Local.CharacterData : null;
                return data != null && data.CurrentProfessionId == _board.ProfessionId;
            }
        }
        // (GetEntries below uses _board.ProfessionId)

        private void EvaluateSubscription() {
            if (!NetworkClient.isConnected) return;

            // On ne s'abonne que si le joueur local est employé dans cette catégorie,
            // pour éviter le refus + notification du career gate serveur. On se désabonne
            // s'il quitte ce métier (changement de carrière).
            if (LocalEmployedHere && !_subscribed) {
                MissionBoardClient.Instance.RequestOpen(_board.ProfessionId);
                _subscribed = true;
            } else if (!LocalEmployedHere && _subscribed) {
                MissionBoardClient.Instance.RequestClose(_board.ProfessionId);
                _subscribed = false;
            }
        }

        private void Render() {
            ClearCards();

            if (cardsContainer == null) {
                GameLogger_LogMissingContainer();
                return;
            }

            // Pas employé ici → board vide (cohérent avec le career gate).
            if (!LocalEmployedHere) return;

            MissionBoardEntry[] entries = MissionBoardClient.Instance.GetEntries(_board.ProfessionId);
            if (entries == null || entries.Length == 0) return;

            int count = Mathf.Min(entries.Length, maxCards);
            for (int i = 0; i < count; i++) {
                MissionBoardEntry e = entries[i];
                MissionDefinition def = MissionDatabase.GetById(e.missionId);
                string title = def != null && !string.IsNullOrEmpty(def.DisplayNameKey)
                    ? def.DisplayNameKey
                    : e.missionId;

                bool available = e.Status == MissionStatus.Available;
                MissionBoardCardView card = MissionBoardCardView.Create(cardsContainer, cardFontSize);
                card.Bind(title, available ? availableColor : activeColor);
                _cards.Add(card);
            }
        }

        private bool _warnedMissingContainer;
        private void GameLogger_LogMissingContainer() {
            if (_warnedMissingContainer) return;
            _warnedMissingContainer = true;
            Debug.LogWarning($"[MissionBoardDisplay] '{name}' : cardsContainer non assigné — " +
                             "assigne le RectTransform de la grille (créée en dur dans la scène).", this);
        }

        private void ClearCards() {
            for (int i = 0; i < _cards.Count; i++) {
                if (_cards[i] != null) Destroy(_cards[i].gameObject);
            }
            _cards.Clear();
        }
    }
}
