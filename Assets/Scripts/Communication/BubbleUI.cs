using Photon.Voice.PUN;
using UnityEngine;

namespace Sim {
    public class BubbleUI : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private GameObject voiceBubble;

        [SerializeField]
        private GameObject writeBubble;

        private PhotonVoiceView photonVoiceView;

        private Canvas canvas;

        private void Awake() {
            this.photonVoiceView = GetComponentInParent<PhotonVoiceView>();
            this.canvas = GetComponent<Canvas>();
            if (canvas != null && canvas.worldCamera == null) {
                canvas.worldCamera = Camera.main;
            }
            
            this.writeBubble.SetActive(false);
            this.voiceBubble.SetActive(false);
        }

        private void Update() {
            if ((photonVoiceView.IsSpeaking || photonVoiceView.IsRecording) && !this.voiceBubble.activeSelf) {
                this.voiceBubble.SetActive(true);
            } else if (!(photonVoiceView.IsSpeaking || photonVoiceView.IsRecording) && this.voiceBubble.activeSelf) {
                this.voiceBubble.SetActive(false);
            }
        }

        private void LateUpdate() {
            if (canvas == null || canvas.worldCamera == null) {
                return;
            } // should not happen, throw error

            transform.rotation = canvas.worldCamera.transform.rotation;
            
            Debug.Log(canvas.worldCamera.transform.position.y);
        }
    }
}