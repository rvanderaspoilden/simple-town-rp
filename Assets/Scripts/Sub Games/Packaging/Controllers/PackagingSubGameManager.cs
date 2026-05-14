using System;
using System.Collections.Generic;
using UnityEngine;

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
        /// Receive null si le joueur annule sans valider.
        /// </summary>
        public static event Action<PackageScore?> OnPackageValidated;

        [Header("Scene refs")]
        [SerializeField] private Canvas uiCanvas;
        [SerializeField] private PackageGridView gridView;
        [SerializeField] private PackageItemTrayView trayView;
        [SerializeField] private PackageInputController input;
        [SerializeField] private PackagingFeedback feedback;
        [SerializeField] private PackagingSubGameConfig fallbackConfig;

        private PackagingSubGameConfig _config;
        private PackageGrid _grid;
        private readonly List<PackageItemInstance> _instances = new List<PackageItemInstance>();
        private bool _validated;

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
            input.Bind(_grid, gridView, trayView, feedback, uiCamera);
            input.OnItemPlaced += HandleItemPlaced;

            if (feedback != null) feedback.HideResultPanel();
            _gameStarted = true;
            _validated = false;
        }

        public override void StopGame() {
            if (input != null) {
                input.OnItemPlaced -= HandleItemPlaced;
                input.Unbind();
            }
            // Si on quitte sans avoir validé, on prévient les abonnés.
            if (!_validated) OnPackageValidated?.Invoke(null);
            base.StopGame();
        }

        private void HandleItemPlaced(PackageItemInstance item) {
            feedback?.PlayPlaceFeedback(item);
            if (trayView != null) trayView.HideView(item.Id);
        }

        /// <summary>
        /// Câblable sur le bouton "Valider le colis" de l'UI.
        /// </summary>
        public void ValidatePackage() {
            if (_validated || _grid == null) return;
            // Permettre la validation même si tous les items ne sont pas placés —
            // wholesome : on ne bloque pas. Le score sera juste plus bas.
            var score = PackageScoringSystem.Evaluate(_grid, _config, _instances.Count);
            _validated = true;
            feedback?.PlayValidationFeedback(score);
            OnPackageValidated?.Invoke(score);
            // Laisse le panel de résultat visible un court instant. Un bouton
            // "Continuer" sur le panel devra appeler StopGame() via une UnityEvent.
        }

        /// <summary>
        /// À câbler sur le bouton "Continuer" / "Parfait !" du panel de résultat.
        /// </summary>
        public void CloseAfterValidation() {
            StopGame();
        }
    }
}
