using UnityEngine;

namespace Sim {
    public class CursorManager : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private Texture2D buildCursor;
        
        [Header("Debug")]
        [SerializeField]
        private Texture2D currentCursor;

        public static CursorManager Instance;

        private void Awake() {
            if (Instance != null) {
                Destroy(this.gameObject);
            }

            Instance = this;
            DontDestroyOnLoad(this.gameObject);
        }

        public void SetCursor(Texture2D cursorTexture) {
            this.currentCursor = cursorTexture;
            Cursor.SetCursor(cursorTexture, Vector2.zero, CursorMode.Auto);
        }

        public void SetBuildCursor() {
            this.SetCursor(this.buildCursor);
        }

        public Texture2D GetCursor() {
            return this.currentCursor;
        }
    }
}