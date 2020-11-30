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
        
        private Image image;

        public delegate void StateChanged(bool isActive);

        public static event StateChanged OnStateChanged;

        public static LoadingManager Instance;

        private void Awake() {
            if (Instance != null) {
                Destroy(this.gameObject);
            }

            this.image = GetComponentInChildren<Image>();
            this.image.color = new Color(1, 1, 1, 0);

            Instance = this;
            DontDestroyOnLoad(this.gameObject);
        }

        public void Show() {
            this.image.DOColor(new Color(1, 1, 1, 1), this.fadeInDuration);
            OnStateChanged?.Invoke(true);
        }

        public void Hide() {
            this.image.DOColor(new Color(1, 1, 1, 0), this.fadeOutDuration).OnComplete(() => OnStateChanged?.Invoke(false));
        }
    }
}