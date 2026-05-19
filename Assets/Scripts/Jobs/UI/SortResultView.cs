using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.Jobs {
    /// <summary>
    /// Vue affichée pour les jobs scorés par AccuracyBasedScorer
    /// (étape SortItems). Montre le rating, le ratio de tris corrects
    /// et la précision.
    /// </summary>
    public class SortResultView : JobResultView {
        [SerializeField] private TMP_Text ratingLabel;
        [SerializeField] private TMP_Text scoreLabel;
        [SerializeField] private TMP_Text breakdownLabel;
        [SerializeField] private TMP_Text earningsLabel;

        [Header("Rating icon (optionnel)")]
        [SerializeField] private Image ratingIcon;
        [SerializeField] private Sprite okIcon;
        [SerializeField] private Sprite goodIcon;
        [SerializeField] private Sprite perfectIcon;

        public override JobResultVariant Variant => JobResultVariant.Sort;

        public override void Render(JobClientState state) {
            var rating = (JobRating)state.CompletionRating;

            if (ratingLabel != null) ratingLabel.text = RatingText(rating);

            if (scoreLabel != null)
                scoreLabel.text = $"{state.CompletionCorrectCount} / {state.CompletionTotalCount} bien triés";

            if (breakdownLabel != null) {
                int wrong = state.CompletionTotalCount - state.CompletionCorrectCount;
                int pct   = state.CompletionTotalCount > 0
                    ? Mathf.RoundToInt((float)state.CompletionCorrectCount / state.CompletionTotalCount * 100f)
                    : 100;
                breakdownLabel.text = wrong > 0
                    ? $"Précision : {pct}%   |   Erreurs : {wrong}"
                    : $"Précision : {pct}%   |   Sans erreur !";
            }

            if (earningsLabel != null) earningsLabel.text = FormatEarnings(state);

            if (ratingIcon != null) {
                ratingIcon.sprite = rating switch {
                    JobRating.Perfect => perfectIcon,
                    JobRating.Good    => goodIcon,
                    _                 => okIcon,
                };
            }
        }

        private static string RatingText(JobRating rating) => rating switch {
            JobRating.Perfect => "Tri parfait !",
            JobRating.Good    => "Bon tri",
            JobRating.Ok      => "Tri correct",
            _                 => "Tri raté",
        };
    }
}
