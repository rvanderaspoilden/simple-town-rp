using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sim.SubGames.Packaging {
    /// <summary>
    /// Orchestrateur du mini-jeu d'emballage. Vit dans la scène additive
    /// chargée par SubGameController. Lit la config via une variable statique
    /// posée par le caller (PackagingMachineBehaviour) avant le LaunchSubGame.
    /// Sur validation, expose le score via OnPackageValidated et appelle
    /// StopGame() pour que le SubGameController déclenche l'unload.
    /// </summary>
    public class PackagingSubGameManager : AbstractSubGameManager {
        /// <summary>
        /// Posée par le caller AVANT LaunchSubGame, lue ici dans Awake/StartGame.
        /// Permet de varier la commande sans changer la SO de scène.
        /// </summary>
        public static PackagingSubGameConfig PendingConfig;

        /// <summary>
        /// Délivré côté caller (machine) après la fermeture du mini-jeu.
        /// Reçoit null si le joueur annule sans valider. Le snapshot est ce que
        /// le caller envoie au serveur — le serveur recalcule son score
        /// autoritaire à partir de là, le client ne fait que prévisualiser.
        /// </summary>
        public static event Action<PackagePlacementSnapshot?> OnPackageValidated;

        [Header("Scene refs")]
        [SerializeField] private Canvas uiCanvas;
        [SerializeField] private PackageGridView gridView;
        [SerializeField] private PackageItemTrayView trayView;
        [SerializeField] private PackageOrderPanelView orderPanel;
        [SerializeField] private PackageInputController input;
        [SerializeField] private PackagingFeedback feedback;
        [SerializeField] private PackagingHintPanelView hintPanel;
        [SerializeField] private PackagingSubGameConfig fallbackConfig;

        private PackagingSubGameConfig _config;
        private PackageGrid _grid;
        private readonly List<PackageItemInstance> _instances = new List<PackageItemInstance>();
        private bool _validated;
        private PackagePlacementSnapshot? _pendingSnapshot;

        private void Start() {
            // Test direct : si la scène a été lancée seule (Play depuis Packaging.unity)
            // au lieu d'être chargée additivement par SubGameController, on auto-démarre
            // avec la fallbackConfig pour pouvoir itérer sans passer par toute la chaîne job.
            if (_gameStarted) return;
            if (gameObject.scene != SceneManager.GetActiveScene()) return;
            if (fallbackConfig == null) return;
            Init(null, null);
            StartGame();
        }

        public override void StartGame() {
            base.StartGame();

            _config = PendingConfig != null ? PendingConfig : fallbackConfig;
            if (_config == null || _config.order == null || _config.order.items == null) {
                Debug.LogError("[PackagingSubGameManager] Missing config/order.");
                StopGame();
                return;
            }

            _grid = new PackageGrid(_config.gridWidth, _config.gridHeight);
            _instances.Clear();
            for (int i = 0; i < _config.order.items.Length; i++) {
                _instances.Add(new PackageItemInstance(i, _config.order.items[i]));
            }

            var uiCamera = uiCanvas != null && uiCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? uiCanvas.worldCamera
                : null;

            gridView.Build(_grid, uiCamera);
            trayView.Build(_instances);
            if (orderPanel != null) {
                orderPanel.Build(_config.order);
                orderPanel.UpdateProgress(_grid.Items);
            }
            input.Bind(_grid, gridView, trayView, feedback, uiCamera);
            input.OnItemPlaced += HandleItemPlaced;
            input.OnItemReturned += HandleItemReturned;

            if (feedback != null) {
                feedback.HideResultPanel();
                if (feedback.CloseButton != null) {
                    feedback.CloseButton.onClick.RemoveListener(OnResultCloseClicked);
                    feedback.CloseButton.onClick.AddListener(OnResultCloseClicked);
                }
            }
            hintPanel?.Show();
            _gameStarted = true;
            _validated = false;
            _pendingSnapshot = null;
        }

        public override void StopGame() {
            if (input != null) {
                input.OnItemPlaced -= HandleItemPlaced;
                input.OnItemReturned -= HandleItemReturned;
                input.Unbind();
            }
            if (feedback != null && feedback.CloseButton != null) {
                feedback.CloseButton.onClick.RemoveListener(OnResultCloseClicked);
            }
            // Si on quitte sans avoir validé, on prévient les abonnés.
            if (!_validated) OnPackageValidated?.Invoke(null);
            base.StopGame();
        }

        private void HandleItemPlaced(PackageItemInstance item) {
            feedback?.PlayPlaceFeedback(item);
            if (orderPanel != null) orderPanel.UpdateProgress(_grid.Items);
        }

        private void HandleItemReturned(PackageItemInstance item) {
            if (orderPanel != null) orderPanel.UpdateProgress(_grid.Items);
        }

        /// <summary>
        /// Câblable sur le bouton "Valider le colis" de l'UI.
        /// Affiche la modale de score (avec le détail des règles de calcul).
        /// La fermeture du mini-jeu est déclenchée par le bouton Close de la
        /// modale (voir OnResultCloseClicked), pas immédiatement ici.
        /// </summary>
        public void ValidatePackage() {
            if (_validated || _grid == null) return;
            // Permettre la validation même si tous les items ne sont pas placés —
            // wholesome : on ne bloque pas. Le score sera juste plus bas.
            var previewScore = PackageScoringSystem.Evaluate(_grid, _config, _instances.Count);
            _validated = true;
            _pendingSnapshot = BuildSnapshot();
            hintPanel?.Hide();
            feedback?.ShowValidationResult(previewScore);
            if (input != null) {
                input.OnItemPlaced -= HandleItemPlaced;
                input.OnItemReturned -= HandleItemReturned;
                input.Unbind();
            }
        }

        /// <summary>
        /// Câblé en interne sur le bouton Close de la modale de résultat.
        /// Envoie le snapshot aux abonnés (machine → serveur) puis ferme le
        /// mini-jeu.
        /// </summary>
        private void OnResultCloseClicked() {
            if (_pendingSnapshot.HasValue) {
                OnPackageValidated?.Invoke(_pendingSnapshot.Value);
                _pendingSnapshot = null;
            }
            StopGame();
        }

        private PackagePlacementSnapshot BuildSnapshot() {
            var placements = new PackagePlacement[_grid.Items.Count];
            int i = 0;
            foreach (var kvp in _grid.Items) {
                var item = kvp.Value;
                placements[i++] = new PackagePlacement {
                    instanceIndex = (byte)item.Id,
                    originX       = (byte)item.Origin.x,
                    originY       = (byte)item.Origin.y,
                    rotation      = (byte)item.Rotation
                };
            }
            return new PackagePlacementSnapshot {
                orderId    = _config.order != null ? _config.order.orderId : string.Empty,
                gridWidth  = (byte)_config.gridWidth,
                gridHeight = (byte)_config.gridHeight,
                placements = placements
            };
        }
    }
}
