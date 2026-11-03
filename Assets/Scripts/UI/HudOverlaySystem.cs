using System.Collections.Generic;
using UnityEngine;

namespace Sim.UI {
    /// <summary>Couche d'un overlay HUD — pilote l'exclusivité et l'ordre de fermeture.</summary>
    public enum OverlayLayer {
        /// <summary>Overlay plein écran exclusif (inventaire, boutique, constellation…) :
        /// un seul ouvert à la fois, masque le HUD de jeu.</summary>
        Fullscreen,
        /// <summary>Modale par-dessus un plein écran (non exclusif entre elles).</summary>
        Modal,
        /// <summary>Petite popup empilable (confirmation, saisie…).</summary>
        Popup,
        /// <summary>Menu contextuel (radial).</summary>
        Context,
        /// <summary>Champ de saisie (chat).</summary>
        Input,
    }

    /// <summary>Contrat minimal d'un overlay HUD géré par <see cref="HudOverlayManager"/>.
    /// Chaque overlay notifie le manager à son ouverture/fermeture ; le manager applique
    /// l'exclusivité (fermer les autres plein écran), masque le HUD de jeu et gère la hotbar.</summary>
    public interface IHudOverlay {
        OverlayLayer Layer { get; }
        /// <summary>Masque les éléments HUD de jeu (vitals, minimap, réticule…) tant qu'ouvert.</summary>
        bool HidesGameHud { get; }
        /// <summary>Affiche la barre d'accès rapide (hotbar) dans l'overlay (inventaire).</summary>
        bool ShowsHotbar { get; }
        /// <summary>Ferme cet overlay (appelé par le manager pour l'exclusivité / Escape).
        /// Doit emprunter le même chemin que la fermeture utilisateur (cleanup, anim…).</summary>
        void CloseOverlay();
    }

    /// <summary>
    /// Gestion centralisée des overlays HUD : une pile unique, l'exclusivité des overlays
    /// plein écran (ouvrir l'un ferme les autres → plus de chevauchement), le masquage
    /// uniforme du HUD de jeu et le placement de la hotbar. Instancié et configuré par
    /// <see cref="HUDManager"/> ; les overlays le notifient via <see cref="OnOpened"/> /
    /// <see cref="OnClosed"/>.
    /// </summary>
    public class HudOverlayManager {
        public static HudOverlayManager Instance { get; private set; }

        private readonly List<IHudOverlay> _stack = new List<IHudOverlay>();

        private readonly GameObject[]  _gameHud;      // éléments HUD de jeu masqués pendant un overlay HidesGameHud
        private readonly RectTransform _hotbar;
        private readonly RectTransform _hotbarSlot;   // hôte de la hotbar dans l'overlay (inventaire)
        private readonly Transform     _hotbarHome;
        private readonly int           _hotbarHomeIndex = -1;
        private readonly List<GameObject> _hidden = new List<GameObject>();

        public HudOverlayManager(GameObject[] gameHud, RectTransform hotbar, RectTransform hotbarSlot) {
            _gameHud = gameHud;
            _hotbar = hotbar;
            _hotbarSlot = hotbarSlot;
            if (_hotbar != null) { _hotbarHome = _hotbar.parent; _hotbarHomeIndex = _hotbar.GetSiblingIndex(); }
            Instance = this;
        }

        /// <summary>True si au moins un overlay plein écran est ouvert.</summary>
        public bool AnyFullscreenOpen {
            get { foreach (var o in _stack) if (o.Layer == OverlayLayer.Fullscreen) return true; return false; }
        }

        /// <summary>Notifie l'ouverture d'un overlay. Un overlay plein écran ferme d'abord
        /// tous les autres plein écran (exclusivité).</summary>
        public void OnOpened(IHudOverlay o) {
            if (o == null || _stack.Contains(o)) return;
            if (o.Layer == OverlayLayer.Fullscreen) {
                // Fermer les autres plein écran AVANT d'empiler o. On retire d'abord de la pile
                // pour que le OnClosed renvoyé par leur CloseOverlay soit un no-op (pas de double
                // Recompute, pas de ré-affichage transitoire du HUD).
                var toClose = new List<IHudOverlay>();
                foreach (var x in _stack) if (x.Layer == OverlayLayer.Fullscreen) toClose.Add(x);
                foreach (var x in toClose) { _stack.Remove(x); x.CloseOverlay(); }
            }
            _stack.Add(o);
            Recompute();
        }

        /// <summary>Notifie la fermeture d'un overlay.</summary>
        public void OnClosed(IHudOverlay o) {
            if (o == null) return;
            if (_stack.Remove(o)) Recompute();
        }

        /// <summary>Ferme le plein écran ouvert le plus récent (Escape). Renvoie vrai si fermé.</summary>
        public bool CloseTopFullscreen() {
            for (int i = _stack.Count - 1; i >= 0; i--)
                if (_stack[i].Layer == OverlayLayer.Fullscreen) { _stack[i].CloseOverlay(); return true; }
            return false;
        }

        private void Recompute() {
            bool hideHud = false, showHotbar = false;
            foreach (var o in _stack) {
                if (o.HidesGameHud) hideHud = true;
                if (o.ShowsHotbar)  showHotbar = true;
            }

            // HUD de jeu : masquer une seule fois, restaurer uniquement ce qu'on a masqué.
            if (hideHud) {
                if (_hidden.Count == 0 && _gameHud != null)
                    foreach (var g in _gameHud)
                        if (g != null && g.activeSelf) { g.SetActive(false); _hidden.Add(g); }
            } else {
                foreach (var g in _hidden) if (g != null) g.SetActive(true);
                _hidden.Clear();
            }

            // Hotbar : visible+reparentée dans l'overlay si demandé ; sinon masquée quand le
            // HUD de jeu l'est, visible à sa place d'origine sinon.
            if (_hotbar != null) {
                if (showHotbar) {
                    if (_hotbarSlot != null && _hotbar.parent != _hotbarSlot) {
                        _hotbar.SetParent(_hotbarSlot, false);
                        _hotbar.SetAsLastSibling();
                    }
                    _hotbar.gameObject.SetActive(true);
                } else {
                    if (_hotbarSlot != null && _hotbar.parent == _hotbarSlot && _hotbarHome != null) {
                        _hotbar.SetParent(_hotbarHome, false);
                        _hotbar.SetSiblingIndex(_hotbarHomeIndex);
                    }
                    _hotbar.gameObject.SetActive(!hideHud);
                }
            }
        }
    }
}
