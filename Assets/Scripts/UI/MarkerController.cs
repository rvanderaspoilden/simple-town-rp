using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Sim {
    public class MarkerController : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private Image largestMarker;

        [SerializeField]
        private Image smallestMarker;

        private RectTransform smallestMarkerRectTransform;

        private RectTransform largestMarkerRectTransform;

        private Sequence sequence;

        private bool active;
        
        public static MarkerController Instance;

        private void Awake() {
            if (Instance != null) {
                Destroy(this.gameObject);
            }

            Instance = this;
            
            this.smallestMarkerRectTransform = this.smallestMarker.GetComponent<RectTransform>();
            this.largestMarkerRectTransform = this.largestMarker.GetComponent<RectTransform>();

            this.sequence = DOTween.Sequence();
            this.sequence.Append(smallestMarker.DOColor(new Color(1, 1, 1, 1), 1f).From(new Color(0, 0, 0, 0)));
            this.sequence.Join(smallestMarkerRectTransform.DOSizeDelta(this.largestMarkerRectTransform.sizeDelta, 1.5f));
            this.sequence.SetLoops(-1);

            this.Hide();
            
            DontDestroyOnLoad(this.gameObject);
        }

        public void ShowAt(Vector3 position) {
            this.transform.position = position + new Vector3(0, 0.05f, 0);
            this.largestMarker.enabled = true;
            this.smallestMarker.enabled = true;
            this.sequence.Play();
            this.active = true;
        }

        public bool IsActive() {
            return this.active;
        }

        public void Hide() {
            this.active = false;
            this.sequence.Pause();
            this.largestMarker.enabled = false;
            this.smallestMarker.enabled = false;
        }
    }
}