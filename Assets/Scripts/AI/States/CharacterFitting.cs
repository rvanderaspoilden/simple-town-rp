using Sim;

namespace AI.States {
    /// <summary>
    /// État CABINE D'ESSAYAGE — verrouillé : rien n'en sort sauf la MORT (un état tiers prend
    /// la main → <see cref="OnExit"/> referme la cabine proprement) ou une ACTION DE L'HUD
    /// (Acheter/Annuler/Échap ferment le panneau → <see cref="Tick"/> repasse Idle).
    /// Pose <see cref="PlayerState.BUSY"/> (hotbar/tir/mêlée neutralisés) ; le mouvement
    /// clavier est bloqué au chokepoint <c>HandleKeyboardMovementEntry</c>, et l'ouverture
    /// d'inventaire/téléphone est inhibée tant que la cabine est ouverte (exclusivité des
    /// overlays plein écran : les laisser s'ouvrir fermerait la cabine).
    /// </summary>
    public class CharacterFitting : IState {
        private readonly PlayerController player;

        public CharacterFitting(PlayerController player) {
            this.player = player;
        }

        public void OnEnter() {
            this.player.PlayerState = PlayerState.BUSY;
        }

        public void Tick() {
            // Seule sortie normale : le panneau s'est refermé (Acheter / Annuler / Échap).
            if (Sim.UI.FittingRoomUI.Instance == null || !Sim.UI.FittingRoomUI.Instance.IsOpen) {
                this.player.Idle();
            }
        }

        public void OnExit() {
            // Sortie FORCÉE (mort, knockdown…) : la cabine est peut-être encore ouverte —
            // on la referme (restaure le vrai look + rend la caméra). No-op sur la sortie
            // normale (panneau déjà fermé).
            if (Sim.UI.FittingRoomUI.Instance != null && Sim.UI.FittingRoomUI.Instance.IsOpen) {
                Sim.UI.FittingRoomUI.Instance.Hide();
            }
        }
    }
}
