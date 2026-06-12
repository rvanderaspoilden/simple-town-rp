using UnityEngine;

namespace Sim.Missions {
    /// <summary>
    /// Émet un toast flottant « Mission terminée » avec les gains à la fin d'une
    /// mission complétée. Pure bridge event → toast, auto-câblé au démarrage runtime.
    ///
    /// L'ancienne modale dédiée a été retirée (redondante avec le toast). On
    /// n'a plus rien à porter en scène : <see cref="MissionClientManager.Instance"/>
    /// est un singleton C# pur, et le bridge se branche dessus via
    /// <c>RuntimeInitializeOnLoadMethod</c> avant que le réseau ne soit prêt.
    /// </summary>
    public static class MissionCompletionToastBridge {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Bootstrap() {
            // Idempotent : un -=/+= en cas de domain reload (Editor) garantit
            // qu'on n'accumule pas plusieurs callbacks.
            MissionClientManager.Instance.MissionFinished -= OnMissionFinished;
            MissionClientManager.Instance.MissionFinished += OnMissionFinished;
        }

        private static void OnMissionFinished(MissionClientState state) {
            if (state.Status != MissionStatus.Completed) return;

            int money = state.CompletionMoneyEarned;
            var constellationLabels  = state.CompletionConstellationLabels;
            var constellationAmounts = state.CompletionConstellationAmounts;
            bool hasConstellation = constellationLabels != null && constellationLabels.Length > 0;
            if (money <= 0 && !hasConstellation) return;

            WorldToastManager.ShowSuccess("Mission terminée",
                FormatRewardSubtitle(money, constellationLabels, constellationAmounts),
                delay: 0.25f);
        }

        /// <summary>Ligne de gains pour le toast : "+1200 €   +2 Ingénieux   +5 Livreur"
        /// (omet ce qui vaut 0). V3 : XP retiré du jeu, plus jamais surfacé ici.</summary>
        private static string FormatRewardSubtitle(int money, string[] constellationLabels, int[] constellationAmounts) {
            var sb = new System.Text.StringBuilder();
            if (money > 0) sb.Append('+').Append(money).Append(" €");
            if (constellationLabels != null && constellationAmounts != null) {
                int n = System.Math.Min(constellationLabels.Length, constellationAmounts.Length);
                for (int i = 0; i < n; i++) {
                    int amount = constellationAmounts[i];
                    if (amount <= 0) continue;
                    var label = constellationLabels[i];
                    if (string.IsNullOrEmpty(label)) continue;
                    if (sb.Length > 0) sb.Append("   ");
                    sb.Append('+').Append(amount).Append(' ').Append(label);
                }
            }
            return sb.ToString();
        }
    }
}
