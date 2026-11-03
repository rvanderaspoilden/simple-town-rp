using Sim.UI;
using UnityEngine;

namespace Sim.Utils {
    /// <summary>
    /// Gameplay cursor lock for the TPS (shoulder) camera mode. The project has no central
    /// "a modal is open" registry, so this polls the known blocking panels; CameraManager calls
    /// Apply() every frame while it owns the decision. Writes to Cursor.* only on transitions so
    /// it doesn't fight other writers (e.g. the loading screen lock in CursorManager).
    /// </summary>
    public static class CursorLock {
        private static bool applied;
        private static bool locked;

        /// <summary>True when a UI panel that needs the mouse is open — the cursor must stay
        /// free and the TPS mouse-look suspended. Prototype list: the main panels only; any
        /// Escape-closable modal missed here keeps working, just with a locked cursor.</summary>
        public static bool IsBlockingUiOpen() {
            HUDManager hud = HUDManager.Instance;
            if (hud != null) {
                if (hud.IsContextMenuOpen) return true;
                if (hud.InventoryUI != null && hud.InventoryUI.gameObject.activeSelf) return true;
            }

            if (PhoneControllerUI.Instance != null && PhoneControllerUI.Instance.IsOpened) return true;
            if (ChatInputUI.Instance != null && ChatInputUI.Instance.IsOpen) return true;
            if (DialogueUI.Instance != null && DialogueUI.Instance.gameObject.activeSelf) return true;
            if (MerchantShopUI.Instance != null && MerchantShopUI.Instance.gameObject.activeSelf) return true;
            if (AtmUI.Instance != null && AtmUI.Instance.IsOpen) return true;
            // HUD de mort : le curseur doit rester libre pour le bouton « Réapparaître ».
            if (DeathScreenUI.Instance != null && DeathScreenUI.Instance.IsOpen) return true;
            // Grille de FOUILLE (loot de cadavre/sac) : elle s'affiche SEULE depuis que
            // l'inventaire ne s'ouvre plus avec — le curseur doit quand même être libéré.
            if (ContainerPanelUI.IsPropLootOpen()) return true;

            return IsTypingInInputField();
        }

        /// <summary>Locks/unlocks + hides/shows the hardware cursor. Idempotent par intention,
        /// MAIS auto-correcteur : si un AUTRE writer (ex. CursorManager en fin de chargement /
        /// téléport) a changé le curseur dans le dos de CursorLock, l'état matériel diverge de
        /// l'intention mémorisée — on réécrit alors même si l'intention n'a pas changé. Sans ce
        /// filet, un curseur libéré par l'écran de chargement resterait visible en gameplay
        /// (le cache pensait « déjà verrouillé » et ne réécrivait jamais).</summary>
        public static void Apply(bool wantLocked) {
            CursorLockMode wantState = wantLocked ? CursorLockMode.Locked : CursorLockMode.None;
            bool hardwareDiverged = Cursor.lockState != wantState || Cursor.visible == wantLocked;
            if (applied && wantLocked == locked && !hardwareDiverged) return;

            applied = true;
            locked = wantLocked;
            Cursor.lockState = wantState;
            Cursor.visible = !wantLocked;
        }

        /// <summary>Oublie l'état mémorisé pour forcer la prochaine <see cref="Apply"/> à
        /// réécrire le curseur. À appeler quand un autre système a pris la main sur le curseur
        /// (fin d'écran de chargement / téléport) pour que le gameplay reprenne proprement.</summary>
        public static void Invalidate() {
            applied = false;
        }

        /// <summary>True when a UI text input field currently has keyboard focus. Public :
        /// aussi utilisé pour inhiber les raccourcis clavier globaux (ex. I = inventaire).</summary>
        public static bool IsTypingInInputField() {
            UnityEngine.EventSystems.EventSystem es = UnityEngine.EventSystems.EventSystem.current;
            GameObject sel = es != null ? es.currentSelectedGameObject : null;
            if (sel == null) return false;
            if (sel.TryGetComponent(out TMPro.TMP_InputField tmp)) return tmp.isFocused;
            if (sel.TryGetComponent(out UnityEngine.UI.InputField legacy)) return legacy.isFocused;
            return false;
        }
    }
}
