using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.Jobs {
    /// <summary>
    /// Vue affichée pour les jobs scorés par MiniGamePassthroughScorer
    /// (ex. mini-jeu d'emballage). Montre le rating et la durée totale.
    /// </summary>
    public class MiniGameResultView : JobResultView {
        [SerializeField] private TMP_Text ratingLabel;
        [SerializeField] private TMP_Text durationLabel;
        [SerializeField] private TMP_Text earningsLabel;

        [Header("Rating icon (optionnel)")]
        [SerializeField] private Image ratingIcon;
        [SerializeField] private Sprite okIcon;
        [SerializeField] private Sprite goodIcon;
        [SerializeField] private Sprite perfectIcon;

        public override JobResultVariant Variant => JobResultVariant.MiniGame;

        public override void Render(JobClientState state) {
            var rating = (JobRating)state.CompletionRating;

            if (ratingLabel != null) ratingLabel.text = RatingText(rating);

            if (durationLabel != null) {
                int total   = Mathf.RoundToInt(state.CompletionElapsedSeconds);
                int minutes = total / 60;
                int seconds = total % 60;
                durationLabel.text = $"Durée : {minutes:00}:{seconds:00}";
            }

            if (earningsLabel != null) {
                earningsLabel.text = state.CompletionMoneyEarned > 0
                    ? $"Gagné : +{state.CompletionMoneyEarned} €"
                    : string.Empty;
            }

            if (ratingIcon != null) {
                ratingIcon.sprite = rating switch {
                    JobRating.Perfect => perfectIcon,
                    JobRating.Good    => goodIcon,
                    _                 => okIcon,
                };
            }
        }

        private static string RatingText(JobRating rating) => rating switch {
            JobRating.Perfect => "Emballage parfait !",
            JobRating.Good    => "Bel emballage",
            JobRating.Ok      => "Colis prêt",
            _                 => "Colis bricolé",
        };
    }
}
