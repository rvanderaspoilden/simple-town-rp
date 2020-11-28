using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Sim {
    public class LoadingManager : MonoBehaviour {
        private Image image;

        public static LoadingManager Instance;

        private void Awake() {
            if (Instance != null) {
                Destroy(this.gameObject);
            }

            this.image = GetComponentInChildren<Image>();
            this.image.color = new Color(1,1,1,0);

            Instance = this;
            DontDestroyOnLoad(this.gameObject);
        }

        public void Show() {
            this.image.DOColor(new Color(1, 1, 1, 1), 0.2f);
        }

        public void Hide() {
            this.image.DOColor(new Color(1, 1, 1, 0), 0.5f);
        }
    }
}