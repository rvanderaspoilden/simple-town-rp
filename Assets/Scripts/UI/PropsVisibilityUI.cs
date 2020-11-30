using Sim.Enums;
using UnityEngine;
using UnityEngine.UI;

namespace Sim {
    public class PropsVisibilityUI : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField]
        private Sprite propsHiddenSprite;

        [SerializeField]
        private Sprite propsFullSprite;

        private Image image;

        private Button button;

        private void Awake() {
            this.image = GetComponent<Image>();
            this.button = GetComponent<Button>();
        }

        private void Start() {
            this.button.onClick.AddListener(() => RoomManager.Instance.TogglePropsVisible());

            RoomManager.OnPropsVisibilityModeChanged += this.UpdateGraphic;
        }

        private void OnDestroy() {
            this.button.onClick.RemoveAllListeners();
            
            RoomManager.OnPropsVisibilityModeChanged -= this.UpdateGraphic;
        }

        private void UpdateGraphic(VisibilityModeEnum mode) {
            this.image.sprite = mode == VisibilityModeEnum.FORCE_HIDE ? this.propsHiddenSprite : this.propsFullSprite;
        }
    }

}
