using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Vue d'affichage de résultat de mission. Une sous-vue par
    /// JobResultVariant est posée comme enfant du
    /// JobCompletionResultPanel ; le panneau active la bonne en fonction
    /// du scorer ayant produit le rating.
    /// </summary>
    public abstract class JobResultView : MonoBehaviour {
        public abstract JobResultVariant Variant { get; }
        public abstract void Render(JobClientState state);

        /// <summary>
        /// Texte récapitulatif des récompenses (money + XP). Vide si rien
        /// gagné. Partagé par toutes les sous-vues pour rester cohérent
        /// quand une mission cumule plusieurs RewardDefinition.
        /// </summary>
        protected static string FormatEarnings(JobClientState state) {
            if (state == null) return string.Empty;
            bool hasMoney = state.CompletionMoneyEarned > 0;
            bool hasXp    = state.CompletionXpEarned    > 0;
            if (!hasMoney && !hasXp) return string.Empty;

            var sb = new System.Text.StringBuilder("Gagné : ");
            if (hasMoney) sb.Append('+').Append(state.CompletionMoneyEarned).Append(" €");
            if (hasMoney && hasXp) sb.Append("   ");
            if (hasXp)    sb.Append('+').Append(state.CompletionXpEarned).Append(" XP");
            return sb.ToString();
        }
    }
}
