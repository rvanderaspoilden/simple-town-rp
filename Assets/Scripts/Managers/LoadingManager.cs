using System;
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
        private Image logoImg;

        private CanvasGroup _canvasGroup;

        public delegate void StateChanged(bool isActive);

        public static event StateChanged OnStateChanged;

        public static LoadingManager Instance;

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(this.gameObject);
            } else {
                Instance = this;
                this._canvasGroup = GetComponent<CanvasGroup>();
                this.Hide(true);
                DontDestroyOnLoad(this.gameObject);
            }
        }

        public void Show(bool instant = false, Action action = null) {
            this._canvasGroup.interactable = true;
            this._canvasGroup.blocksRaycasts = true;
            this._canvasGroup.DOFade(1, instant ? 0 : this.fadeInDuration).OnComplete(() => action?.Invoke());
            OnStateChanged?.Invoke(true);
        }

        public void Hide(bool instant = false) {
            this._canvasGroup.interactable = true;
            this._canvasGroup.blocksRaycasts = false;
            this._canvasGroup.DOFade(0, instant ? 0 : this.fadeOutDuration).OnComplete(() => OnStateChanged?.Invoke(false));
        }
    }
}