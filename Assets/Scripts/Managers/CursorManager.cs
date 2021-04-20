using System;
using Sim.Building;
using Sim.Enums;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace Sim {
    public class CursorManager : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private Texture2D buildCursor;

        [SerializeField]
        private Texture2D socialCursor;

        [SerializeField]
        private Texture2D rotationCursor;

        [SerializeField]
        private Texture2D moveCursor;

        [Header("Debug")]
        [SerializeField]
        private Texture2D currentCursor;

        private new Camera camera;

        private RaycastHit hit;

        public static CursorManager Instance;

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(this.gameObject);
            } else {
                Instance = this;
            }

            DontDestroyOnLoad(this.gameObject);

            this.SetCamera(Camera.main);

            SceneManager.sceneLoaded += SceneLoaded;
            LoadingManager.OnStateChanged += LoadingStateChanged;
        }

        private void OnDestroy() {
            SceneManager.sceneLoaded -= SceneLoaded;
            LoadingManager.OnStateChanged -= LoadingStateChanged;
        }

        private void Update() {
            if (!CameraManager.Instance) return;

            Ray ray = this.camera.ScreenPointToRay(Input.mousePosition);

            if (CameraManager.Instance.GetMode() == CameraModeEnum.FREE && !EventSystem.current.IsPointerOverGameObject() &&
                Physics.Raycast(ray.origin, ray.direction, out hit, 100)) {
                if (!this.IsProps() && !this.IsCharacter()) {
                    this.SetCursor(null);
                }
            } else if (CameraManager.Instance.GetMode() == CameraModeEnum.BUILD && Input.GetMouseButton(2)) {
                this.SetCursor(this.rotationCursor);
            } else if (CameraManager.Instance.GetMode() == CameraModeEnum.BUILD &&
                       (Input.GetMouseButton(1) || Input.GetAxis("Horizontal") != 0f || Input.GetAxis("Vertical") != 0f)) {
                this.SetCursor(this.moveCursor);
            } else {
                this.SetCursor(null);
            }
        }

        private void SetCursor(Texture2D cursorTexture) {
            this.currentCursor = cursorTexture;
            Cursor.SetCursor(cursorTexture, Vector2.zero, CursorMode.Auto);
        }

        public Texture2D GetCursor() {
            return this.currentCursor;
        }

        private void LoadingStateChanged(bool isActive) {
            Cursor.lockState = isActive ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !isActive;
        }

        private bool IsProps() {
            Props props = hit.collider.GetComponentInParent<Props>();

            if (props) {
                this.SetCursor(props.IsBuilt() ? props.GetConfiguration().GetCursor() : this.buildCursor);
                return true;
            }

            return false;
        }

        private bool IsCharacter() {
            PlayerController player = hit.collider.GetComponent<PlayerController>();

            if (player != null && player != PlayerController.Local) {
                this.SetCursor(this.socialCursor);
                return true;
            }

            return false;
        }

        private void SceneLoaded(Scene scene, LoadSceneMode mode) {
            if (mode == LoadSceneMode.Single) this.SetCamera(Camera.main);
        }

        private void SetCamera(Camera camera) {
            this.camera = camera;
        }
    }
}