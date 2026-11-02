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

            return IsTypingInInputField();
        }

        /// <summary>Locks/unlocks + hides/shows the hardware cursor. Idempotent: only writes on
        /// a state change.</summary>
        public static void Apply(bool wantLocked) {
            if (applied && wantLocked == locked) return;

            applied = true;
            locked = wantLocked;
            Cursor.lockState = wantLocked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !wantLocked;
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
