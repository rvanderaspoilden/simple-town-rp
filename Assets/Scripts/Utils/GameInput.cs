using UnityEngine.InputSystem;

namespace Sim.Utils {
    /// <summary>
    /// Clavier via le NOUVEAU Input System (Keyboard.current). Les <see cref="Key"/> sont
    /// identifiées par POSITION PHYSIQUE (indépendant du layout AZERTY/QWERTY), contrairement
    /// au KeyCode legacy dont le mapping des lettres suit la position US-QWERTY — source du bug
    /// « A droppe pendant le strafe ». Toutes les touches GAMEPLAY passent par ici ; le legacy
    /// (Input.*) reste pour la souris, les axes build-mode et les touches layout-agnostiques
    /// (Escape, flèches, Enter, F-keys) — le projet est en Active Input Handling = Both.
    /// </summary>
    public static class GameInput {
        /// <summary>Touche pressée cette frame (équivalent GetKeyDown).</summary>
        public static bool Down(Key key) {
            Keyboard kb = Keyboard.current;
            return kb != null && key != Key.None && kb[key].wasPressedThisFrame;
        }

        /// <summary>Touche maintenue (équivalent GetKey).</summary>
        public static bool Held(Key key) {
            Keyboard kb = Keyboard.current;
            return kb != null && key != Key.None && kb[key].isPressed;
        }

        /// <summary>Touche relâchée cette frame (équivalent GetKeyUp).</summary>
        public static bool Up(Key key) {
            Keyboard kb = Keyboard.current;
            return kb != null && key != Key.None && kb[key].wasReleasedThisFrame;
        }

        /// <summary>Axe -1..1 composé de deux touches (équivalent GetAxisRaw sur un couple,
        /// ex. S/W pour l'accélérateur). Combiner plusieurs couples avec Mathf.Clamp.</summary>
        public static float Axis(Key negative, Key positive) {
            float v = 0f;
            if (Held(negative)) v -= 1f;
            if (Held(positive)) v += 1f;
            return v;
        }
    }
}
