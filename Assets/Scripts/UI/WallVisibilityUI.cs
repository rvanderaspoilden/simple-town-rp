using System;
using Sim.Enums;
using UnityEngine;
using UnityEngine.UI;

namespace Sim {
    public class WallVisibilityUI : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private Sprite wallHiddenSprite;

        [SerializeField]
        private Sprite wallFullSprite;

        private Image image;

        private Button button;

        private void Awake() {
            this.image = GetComponent<Image>();
            this.button = GetComponent<Button>();
        }

        private void Start() {
            this.button.onClick.AddListener(() => RoomManager.Instance.ToggleWallVisibility());

            RoomManager.OnWallVisibilityModeChanged += this.UpdateGraphic;
        }

        private void OnDestroy() {
            this.button.onClick.RemoveAllListeners();
            
            RoomManager.OnWallVisibilityModeChanged -= this.UpdateGraphic;
        }

        private void UpdateGraphic(VisibilityModeEnum mode) {
            this.image.sprite = mode == VisibilityModeEnum.FORCE_HIDE ? this.wallHiddenSprite : this.wallFullSprite;
        }
    }
}
