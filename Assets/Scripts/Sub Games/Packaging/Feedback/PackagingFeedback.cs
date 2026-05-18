using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.SubGames.Packaging {
    /// <summary>
    /// Feedback audio/visuel cozy + modale de prévisualisation du score à la
    /// validation. La modale détaille les règles de calcul (espace, fragiles,
    /// lourds, items placés, total/1000) et expose un bouton Close qui ferme
    /// le mini-jeu (le PackagingSubGameManager écoute closeButton.onClick et
    /// déclenche StopGame + dispatch OnPackageValidated).
    /// </summary>
    public class PackagingFeedback : MonoBehaviour {
        [Header("Audio")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip defaultPlaceClip;
        [SerializeField] private AudioClip rejectClip;
        [SerializeField] private AudioClip rotateClip;
        [SerializeField] private AudioClip validateClip;

        [Header("Result panel")]
        [SerializeField] private GameObject resultPanel;
        [SerializeField] private TextMeshProUGUI ratingLabel;
        [SerializeField] private TextMeshProUGUI scoreLabel;
        [SerializeField] private Image ratingIcon;
        [SerializeField] private Sprite correctIcon;
        [SerializeField] private Sprite goodIcon;
        [SerializeField] private Sprite perfectIcon;

        [Header("Score breakdown (calculation rules)")]
        [SerializeField] private TextMeshProUGUI spaceBreakdownLabel;
        [SerializeField] private TextMeshProUGUI fragileBreakdownLabel;
        [SerializeField] private TextMeshProUGUI heavyBreakdownLabel;
        [SerializeField] private TextMeshProUGUI itemsBreakdownLabel;

        [Header("Close")]
        [SerializeField] private Button closeButton;

        /// <summary>Le close button du résultat. Le manager s'y abonne pour fermer le mini-jeu.</summary>
        public Button CloseButton => closeButton;

        public void PlayPlaceFeedback(PackageItemInstance item) {
            var clip = item != null && item.Definition != null && item.Definition.placeSound != null
                ? item.Definition.placeSound
                : defaultPlaceClip;
            PlayOne(clip);
        }

        public void PlayRotateFeedback() => PlayOne(rotateClip);
        public void PlayRejectFeedback() => PlayOne(rejectClip);

        public void ShowValidationResult(PackageScore score) {
            PlayOne(validateClip);

            if (resultPanel != null) resultPanel.SetActive(true);

            if (ratingLabel != null) ratingLabel.text = RatingText(score.rating);
            if (scoreLabel != null)  scoreLabel.text  = $"{score.total} / 1000";

            if (ratingIcon != null) {
                ratingIcon.sprite = score.rating switch {
                    PackageRating.Perfect => perfectIcon,
                    PackageRating.Good    => goodIcon,
                    _                     => correctIcon
                };
            }

            if (spaceBreakdownLabel != null)
                spaceBreakdownLabel.text = $"Espace : {Mathf.RoundToInt(score.spaceRatio * 100)}%";
            if (fragileBreakdownLabel != null)
                fragileBreakdownLabel.text = score.fragileOk
                    ? "Fragiles protégés : OK"
                    : "Fragiles protégés : KO";
            if (heavyBreakdownLabel != null)
                heavyBreakdownLabel.text = score.heavyOk
                    ? "Lourds bien posés : OK"
                    : "Lourds bien posés : KO";
            if (itemsBreakdownLabel != null) {
                itemsBreakdownLabel.text = score.allItemsPlaced
                    ? $"Items placés : {score.totalCount} / {score.totalCount}"
                    : $"Items placés : {score.placedCount} / {score.totalCount}";
            }
        }

        public void HideResultPanel() {
            if (resultPanel != null) resultPanel.SetActive(false);
        }

        private void PlayOne(AudioClip clip) {
            if (clip != null && audioSource != null) audioSource.PlayOneShot(clip);
        }

        private static string RatingText(PackageRating rating) => rating switch {
            PackageRating.Perfect => "Emballage Parfait !",
            PackageRating.Good    => "Bel emballage",
            _                     => "Colis prêt"
        };
    }
}
