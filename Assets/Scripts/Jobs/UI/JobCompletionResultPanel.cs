using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Émet un toast flottant « Mission terminée » avec les gains à la fin d'une
    /// mission complétée. La modale dédiée a été retirée (redondante avec le toast) ;
    /// ce composant reste en scène uniquement comme handler d'event.
    ///
    /// Le GameObject porteur DOIT rester actif pour que <c>Awake</c> exécute
    /// l'abonnement.
    /// </summary>
    public class JobCompletionResultPanel : MonoBehaviour {
        public static JobCompletionResultPanel Instance { get; private set; }

        private bool _subscribed;

        private void Awake() {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Subscribe();
        }

        private void OnDestroy() {
            Unsubscribe();
            if (Instance == this) Instance = null;
        }

        private void Subscribe() {
            if (_subscribed) return;
            JobClientManager.Instance.JobFinished += OnJobFinished;
            _subscribed = true;
        }

        private void Unsubscribe() {
            if (!_subscribed) return;
            JobClientManager.Instance.JobFinished -= OnJobFinished;
            _subscribed = false;
        }

        private void OnJobFinished(JobClientState state) {
            if (state.Status != JobStatus.Completed) return;

            int money = state.CompletionMoneyEarned;
            int xp    = state.CompletionXpEarned;
            if (money <= 0 && xp <= 0) return;

            WorldToastManager.Show("Mission terminée", FormatRewardSubtitle(money, xp), delay: 0.25f);
        }

        /// <summary>Ligne de gains pour le toast : "+1200 €   +30 XP ⭐" (omet ce qui vaut 0).</summary>
        private static string FormatRewardSubtitle(int money, int xp) {
            var sb = new System.Text.StringBuilder();
            if (money > 0) sb.Append('+').Append(money).Append(" €");
            if (money > 0 && xp > 0) sb.Append("   ");
            if (xp > 0) sb.Append('+').Append(xp).Append(" XP ⭐");
            return sb.ToString();
        }
    }
}
