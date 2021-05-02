using Dissonance;
using UnityEngine;

namespace Sim {
    public class BubbleUI : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private GameObject voiceBubble;

        [SerializeField]
        private GameObject writeBubble;

        [SerializeField]
        private float maxPosY;

        [SerializeField]
        private float minPosY;

        [SerializeField]
        private Vector2 maxSizeDelta;

        [SerializeField]
        private float maxCameraPosY;
        
        private Canvas canvas;
        
        private void Awake() {
            this.canvas = GetComponent<Canvas>();
            if (canvas != null && canvas.worldCamera == null) {
                canvas.worldCamera = Camera.main;
            }
            
            this.writeBubble.SetActive(false);
            this.voiceBubble.SetActive(false);
        }

        public void SetVoiceBubbleVisibility(bool isVisible) {
            this.voiceBubble.SetActive(isVisible);
        }

        private void LateUpdate() {
            if (canvas == null || canvas.worldCamera == null) {
                return;
            }

            this.transform.rotation = canvas.worldCamera.transform.rotation;
            
            float posY = Mathf.Clamp(((this.maxPosY * canvas.worldCamera.transform.position.y) / this.maxCameraPosY), this.minPosY, this.maxPosY);

            this.transform.localPosition = new Vector3(this.transform.localPosition.x, posY, this.transform.localPosition.z);

            float scale = posY / this.maxPosY;
            this.transform.localScale = new Vector3(scale, scale ,scale);
        }
    }
}