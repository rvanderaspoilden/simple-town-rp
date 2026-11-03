using Sim.Enums;
using UnityEngine;

namespace Sim {
    /// <summary>
    /// Curseurs custom du mode BUILD (rotation, déplacement, peinture). En TPS le curseur
    /// hardware est verrouillé/masqué (CursorLock) et le feedback de visée passe par le
    /// crosshair — plus de curseurs de survol « click-to-move ».
    /// </summary>
    public class CursorManager : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private Texture2D rotationCursor;

        [SerializeField]
        private Texture2D moveCursor;

        [Header("Paint mode")]
        [SerializeField]
        private Texture2D wallPaintCursor;

        [SerializeField]
        private Texture2D groundPaintCursor;

        [SerializeField]
        private Texture2D eraserCursor;

        [Header("Debug")]
        [SerializeField]
        private Texture2D currentCursor;

        public static CursorManager Instance;

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(this.gameObject);
            } else {
                Instance = this;
            }

            DontDestroyOnLoad(this.gameObject);

            LoadingManager.OnStateChanged += LoadingStateChanged;
        }

        private void OnDestroy() {
            LoadingManager.OnStateChanged -= LoadingStateChanged;
        }

        private void Update() {
            if (!CameraManager.Instance) return;

            bool buildMode = CameraManager.Instance.GetMode() == CameraModeEnum.BUILD;

            // Paint mode wins over the generic build-camera cursors.
            if (buildMode && TrySetPaintCursor()) return;

            if (buildMode && Input.GetMouseButton(2)) {
                this.SetCursor(this.rotationCursor);
            } else if (buildMode &&
                       (Input.GetMouseButton(1) || Input.GetAxis("Horizontal") != 0f || Input.GetAxis("Vertical") != 0f)) {
                this.SetCursor(this.moveCursor);
            } else {
                this.SetCursor(null);
            }
        }

        private bool TrySetPaintCursor() {
            if (BuildManager.Instance == null) return false;

            BuildModeEnum mode = BuildManager.Instance.GetMode();
            bool isWallPaint   = mode == BuildModeEnum.WALL_PAINT;
            bool isGroundPaint = mode == BuildModeEnum.GROUND_PAINT;
            if (!isWallPaint && !isGroundPaint) return false;

            // Right-click drag = eraser; left-click drag uses the paint cursor (auto-invert
            // is per-face so showing the eraser there would flicker).
            if (Input.GetMouseButton(1) && this.eraserCursor != null) {
                this.SetCursor(this.eraserCursor);
                return true;
            }

            Texture2D paint = isWallPaint ? this.wallPaintCursor : this.groundPaintCursor;
            if (paint != null) {
                this.SetCursor(paint);
                return true;
            }
            return false;
        }

        private void SetCursor(Texture2D cursorTexture) {
            this.currentCursor = cursorTexture;
            Cursor.SetCursor(cursorTexture, Vector2.zero, CursorMode.Auto);
        }

        public Texture2D GetCursor() {
            return this.currentCursor;
        }

        private void LoadingStateChanged(bool isActive) {
            if (isActive) {
                // Pendant le chargement : curseur verrouillé/masqué (l'écran couvre la vue).
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            // Fin de chargement (lancement du jeu, téléport) : on NE force PLUS le curseur
            // visible. L'état gameplay (CameraManager.ApplyTpsCursorLock) reprend la main dès
            // la frame suivante — en TPS il reste donc masqué/verrouillé. On invalide le cache
            // de CursorLock pour qu'il réécrive, car ce writer direct l'a désynchronisé.
            Sim.Utils.CursorLock.Invalidate();
        }
    }
}
