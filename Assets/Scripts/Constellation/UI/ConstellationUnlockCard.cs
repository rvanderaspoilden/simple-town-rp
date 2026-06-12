using DG.Tweening;
using TMPro;
using UnityEngine;

namespace Sim.Constellation {
    // Carte « NOUVELLE ÉTOILE » jouée à chaque déblocage. Visuel fourni par le prefab ;
    // ce script joue la séquence DOTween (fade + scale) et le son cristallin.
    public class ConstellationUnlockCard : MonoBehaviour {
        [Header("Refs prefab")]
        [SerializeField] private GameObject root;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private TextMeshProUGUI titleText;     // ex. nom de l'étoile
        [SerializeField] private TextMeshProUGUI subtitleText;  // ex. description courte
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip chimeClip;

        [Header("Réglages")]
        [SerializeField] private float holdSeconds = 1.8f;

        private void Awake() {
            if (root != null) root.SetActive(false);
        }

        public void Play(ConstellationNodeData node) {
            if (root == null || canvasGroup == null) return;

            if (titleText != null) titleText.text = node.displayName;
            if (subtitleText != null) subtitleText.text = node.description;

            root.SetActive(true);
            canvasGroup.alpha = 0f;
            transform.localScale = Vector3.one * 0.6f;

            if (audioSource != null && chimeClip != null) audioSource.PlayOneShot(chimeClip);

            var seq = DOTween.Sequence();
            seq.Append(canvasGroup.DOFade(1f, 0.3f));
            seq.Join(transform.DOScale(1f, 0.5f).SetEase(Ease.OutBack));
            seq.AppendInterval(holdSeconds);
            seq.Append(canvasGroup.DOFade(0f, 0.4f));
            seq.OnComplete(() => root.SetActive(false));
        }
    }
}
