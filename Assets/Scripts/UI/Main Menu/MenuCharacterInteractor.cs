using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sim {
    [RequireComponent(typeof(RawImage))]
    public class MenuCharacterInteractor : MonoBehaviour, IPointerDownHandler {
        [SerializeField]
        private Transform characterTransform;

        [SerializeField]
        private Animator characterAnimator;

        [SerializeField]
        private float rotationSpeed = 10f;

        [SerializeField]
        private float alphaThreshold = 0.1f;

        [SerializeField]
        private float greetingDuration = 3.35f;

        private RawImage rawImage;
        private RectTransform rt;
        private bool rotating;
        private bool greetingPlaying;

        private void Awake() {
            rawImage = GetComponent<RawImage>();
            rt = rawImage.rectTransform;
        }

        public void OnPointerDown(PointerEventData eventData) {
            if (!IsOnCharacter(eventData.position, eventData.pressEventCamera)) return;

            if (eventData.button == PointerEventData.InputButton.Middle) {
                rotating = true;
            } else if (eventData.button == PointerEventData.InputButton.Left) {
                TriggerGreeting();
            }
        }

        private void Update() {
            if (!rotating) return;
            if (!Input.GetMouseButton(2)) { rotating = false; return; }

            float dx = Input.GetAxis("Mouse X");
            if (Mathf.Abs(dx) <= 0f) return;
            if (characterTransform == null) return;

            characterTransform.Rotate(Vector3.up, -dx * rotationSpeed, Space.World);
        }

        private void OnDisable() {
            rotating = false;
        }

        // Samples the RenderTexture alpha under the click. Returns false unless
        // the click hits a visible character pixel — empty / cleared regions of
        // the RawImage rect don't count as "on the character".
        private bool IsOnCharacter(Vector2 screenPos, Camera eventCam) {
            if (!(rawImage.texture is RenderTexture tex)) return true;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screenPos, eventCam, out var local)) return false;

            Rect r = rt.rect;
            float nx = (local.x - r.xMin) / r.width;
            float ny = (local.y - r.yMin) / r.height;
            if (nx < 0f || nx > 1f || ny < 0f || ny > 1f) return false;

            int px = Mathf.Clamp((int)(nx * tex.width), 0, tex.width - 1);
            int py = Mathf.Clamp((int)(ny * tex.height), 0, tex.height - 1);

            var prev = RenderTexture.active;
            RenderTexture.active = tex;
            var sample = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            sample.ReadPixels(new Rect(px, py, 1, 1), 0, 0);
            sample.Apply();
            float alpha = sample.GetPixel(0, 0).a;
            RenderTexture.active = prev;
            Destroy(sample);

            return alpha >= alphaThreshold;
        }

        private void TriggerGreeting() {
            if (characterAnimator == null || greetingPlaying) return;
            StartCoroutine(GreetCoroutine());
        }

        private IEnumerator GreetCoroutine() {
            greetingPlaying = true;
            characterAnimator.SetFloat("Action", (float) CharacterAnimatorAction.GREET);
            yield return new WaitForSeconds(greetingDuration);
            characterAnimator.SetFloat("Action", (float) CharacterAnimatorAction.NONE);
            greetingPlaying = false;
        }
    }
}
