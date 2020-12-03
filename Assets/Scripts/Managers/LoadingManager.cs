using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Sim {
    public class LoadingManager : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        [Min(0)]
        private float fadeInDuration;

        [SerializeField]
        [Min(0)]
        private float fadeOutDuration;

        [SerializeField]
        private float maxLoaderOpacity;

        [SerializeField]
        private float maxFrontOpacity;

        [SerializeField]
        private Image backgroundImg;

        [SerializeField]
        private Image frontImg;

        [SerializeField]
        private Image loaderImg;

        public delegate void StateChanged(bool isActive);

        public static event StateChanged OnStateChanged;

        public static LoadingManager Instance;

        private void Awake() {
            if (Instance != null) {
                Destroy(this.gameObject);
            }

            this.frontImg.color = new Color(1, 1, 1, 0);
            this.backgroundImg.color = new Color(1, 1, 1, 0);
            this.loaderImg.color = new Color(1, 1, 1, 0);

            Instance = this;
            DontDestroyOnLoad(this.gameObject);
        }

        public void Show(bool instant = false) {
            this.backgroundImg.DOColor(new Color(1, 1, 1, 1), instant ? 0 : this.fadeInDuration);
            this.frontImg.DOColor(new Color(1, 1, 1, this.maxFrontOpacity), instant ? 0 : this.fadeInDuration);
            this.loaderImg.DOColor(new Color(1, 1, 1, this.maxLoaderOpacity), instant ? 0 : this.fadeInDuration);
            OnStateChanged?.Invoke(true);
        }

        public void Hide() {
            this.backgroundImg.DOColor(new Color(1, 1, 1, 0), this.fadeOutDuration).OnComplete(() => OnStateChanged?.Invoke(false));
            this.frontImg.DOColor(new Color(1, 1, 1, 0), this.fadeOutDuration);
            this.loaderImg.DOColor(new Color(1, 1, 1, 0), this.fadeOutDuration);
        }
    }
}