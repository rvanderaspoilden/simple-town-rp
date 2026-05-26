using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim {
    public class BubbleUI : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private GameObject voiceBubble;

        [Header("Write indicator (player is typing)")]
        [SerializeField]
        private GameObject writeBubble;

        [Header("Chat message bubble")]
        [SerializeField]
        private GameObject chatText;

        [SerializeField]
        private TextMeshProUGUI chatTextLabel;

        [SerializeField]
        private float chatMessageDuration = 6f;

        [Tooltip("Largeur max du conteneur de chat (unités locales du canvas). Le conteneur s'élargit avec le texte mais ne dépasse pas cette valeur ; au-delà le texte passe à la ligne (hauteur dynamique).")]
        [SerializeField]
        private float maxChatWidth = 2f;

        [Tooltip("Largeur min du conteneur de chat — évite une bulle trop étroite pour un message court.")]
        [SerializeField]
        private float minChatWidth = 0.5f;

        [Header("Bubble vertical placement")]
        [Tooltip("Position Y locale de la bulle quand la caméra est proche (zoom max).")]
        [SerializeField]
        private float minPosY = 2.2f;

        [Tooltip("Position Y locale de la bulle quand la caméra est éloignée (zoom min).")]
        [SerializeField]
        private float maxPosY = 2.7f;

        [Tooltip("Distance caméra → personnage qui mappe vers minPosY (caméra proche).")]
        [SerializeField]
        private float nearCameraDistance = 3f;

        [Tooltip("Distance caméra → personnage qui mappe vers maxPosY (caméra éloignée).")]
        [SerializeField]
        private float farCameraDistance = 10f;

        [Tooltip("Vitesse du lerp de la position Y.")]
        [SerializeField]
        private float posLerpSpeed = 8f;

        private Canvas canvas;

        private Coroutine chatTextRoutine;

        private bool isWriting;

        private float currentPosY;

        private void Awake() {
            this.canvas = GetComponent<Canvas>();
            if (canvas != null && canvas.worldCamera == null) {
                canvas.worldCamera = Camera.main;
            }

            this.currentPosY = this.maxPosY;

            if (this.writeBubble != null) this.writeBubble.SetActive(false);
            if (this.voiceBubble != null) this.voiceBubble.SetActive(false);
            if (this.chatText != null) this.chatText.SetActive(false);
        }

        public void SetVoiceBubbleVisibility(bool isVisible) {
            if (this.voiceBubble != null) this.voiceBubble.SetActive(isVisible);
        }

        /// <summary>
        /// Shows or hides the "is typing" indicator (writeBubble). The indicator
        /// is hidden as soon as a chat message bubble is shown.
        /// </summary>
        public void SetWriting(bool writing) {
            this.isWriting = writing;
            this.RefreshWriteBubble();
        }

        /// <summary>
        /// Displays the chat message bubble (chatText) for chatMessageDuration
        /// seconds. While visible, the write indicator is forced off.
        /// </summary>
        public void ShowChatMessage(string message) {
            if (string.IsNullOrWhiteSpace(message)) return;

            if (this.chatTextLabel != null) this.chatTextLabel.text = message;
            if (this.chatText != null) {
                this.chatText.SetActive(true);

                // Largeur dynamique plafonnée : on dimensionne le label à la largeur
                // préférée du texte (une ligne), bornée par [minChatWidth, maxChatWidth].
                // Sous le max → bulle juste à la taille du texte (1 ligne) ; au-delà →
                // largeur figée au max, le texte passe à la ligne (hauteur dynamique).
                // Le conteneur "Chat Text" suit la largeur du label (VerticalLayoutGroup
                // + ContentSizeFitter), comme avant pour la hauteur.
                if (this.chatTextLabel != null) {
                    float preferredWidth = this.chatTextLabel.GetPreferredValues(message, Mathf.Infinity, Mathf.Infinity).x;
                    float width = Mathf.Clamp(preferredWidth, this.minChatWidth, this.maxChatWidth);
                    RectTransform labelRect = this.chatTextLabel.rectTransform;
                    labelRect.sizeDelta = new Vector2(width, labelRect.sizeDelta.y);
                }

                // ContentSizeFitter ne recalcule pas automatiquement à l'activation ;
                // forcer le rebuild pour que largeur/hauteur s'adaptent au texte.
                LayoutRebuilder.ForceRebuildLayoutImmediate(this.chatText.GetComponent<RectTransform>());
            }

            this.RefreshWriteBubble();

            if (this.chatTextRoutine != null) StopCoroutine(this.chatTextRoutine);
            this.chatTextRoutine = StartCoroutine(this.HideChatMessageAfterDelay());
        }

        public void HideChatMessage() {
            if (this.chatTextRoutine != null) {
                StopCoroutine(this.chatTextRoutine);
                this.chatTextRoutine = null;
            }
            if (this.chatText != null) this.chatText.SetActive(false);
            this.RefreshWriteBubble();
        }

        private IEnumerator HideChatMessageAfterDelay() {
            yield return new WaitForSeconds(this.chatMessageDuration);
            if (this.chatText != null) this.chatText.SetActive(false);
            this.chatTextRoutine = null;
            this.RefreshWriteBubble();
        }

        private void RefreshWriteBubble() {
            if (this.writeBubble == null) return;
            bool chatVisible = this.chatText != null && this.chatText.activeSelf;
            this.writeBubble.SetActive(this.isWriting && !chatVisible);
        }

        public GameObject VoiceBubble => voiceBubble;

        private void LateUpdate() {
            if (canvas == null || canvas.worldCamera == null) {
                return;
            }

            this.transform.rotation = canvas.worldCamera.transform.rotation;

            // Repère relatif au parent de la bulle (sur le perso). Évite que
            // la formule casse quand le perso change d'étage en monde.
            Transform refPoint = this.transform.parent != null ? this.transform.parent : this.transform;
            float dist = Vector3.Distance(canvas.worldCamera.transform.position, refPoint.position);

            // dist proche → minPosY (bulle basse), dist loin → maxPosY (bulle haute).
            float t = Mathf.InverseLerp(this.nearCameraDistance, this.farCameraDistance, dist);
            float targetPosY = Mathf.Lerp(this.minPosY, this.maxPosY, t);

            this.currentPosY = Mathf.Lerp(this.currentPosY, targetPosY, this.posLerpSpeed * Time.deltaTime);

            this.transform.localPosition = new Vector3(this.transform.localPosition.x, this.currentPosY, this.transform.localPosition.z);

            float scale = this.maxPosY > 0f ? this.currentPosY / this.maxPosY : 1f;
            this.transform.localScale = new Vector3(scale, scale, scale);
        }
    }
}
