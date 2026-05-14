using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.SubGames.Packaging {
    /// <summary>
    /// Feedback audio/visuel cozy. Aucune logique gameplay ici : tout est
    /// purement présentation. Reste minimal côté MVP.
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

        public void PlayPlaceFeedback(PackageItemInstance item) {
            var clip = item != null && item.Definition != null && item.Definition.placeSound != null
                ? item.Definition.placeSound
                : defaultPlaceClip;
            PlayOne(clip);
        }

        public void PlayRotateFeedback() => PlayOne(rotateClip);
        public void PlayRejectFeedback() => PlayOne(rejectClip);

        public void PlayValidationFeedback(PackageScore score) {
            PlayOne(validateClip);
            if (resultPanel != null) resultPanel.SetActive(true);
            if (ratingLabel != null) ratingLabel.text = RatingText(score.rating);
            if (scoreLabel != null) scoreLabel.text = $"{score.total} / 1000";
            if (ratingIcon != null) {
                ratingIcon.sprite = score.rating switch {
                    PackageRating.Perfect => perfectIcon,
                    PackageRating.Good    => goodIcon,
                    _                     => correctIcon
                };
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
