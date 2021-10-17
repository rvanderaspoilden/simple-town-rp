using System.Linq;
using Sim;
using UnityEngine;
using UnityEngine.UI;

public class InventoryUI : MonoBehaviour {
    [Header("Settings")]
    [SerializeField]
    private Image leftHandItemIcon;

    [SerializeField]
    private Image rightHandItemIcon;

    [SerializeField]
    private InventoryActionMenu leftHandActionMenu;

    [SerializeField]
    private InventoryActionMenu rightHandActionMenu;

    [Header("Only for debug")]
    [SerializeField]
    private InventoryActionMenu currentActionMenu;

    private void OnEnable() {
        this.UpdateUI();
    }

    private void Update() {
        if (Input.GetKeyDown(KeyCode.Escape)) {
            HUDManager.Instance.CloseInventory();
        }
    }

    public void DisplayLeftActionMenu() {
        this.CloseCurrentActionMenu();

        if (PlayerController.Local.PlayerHands.LeftHandItem == null) return;

        this.leftHandActionMenu.Setup(PlayerController.Local.PlayerHands.LeftHandItem.GetActions().ToList());

        this.currentActionMenu = this.leftHandActionMenu;
    }

    public void DisplayRightActionMenu() {
        this.CloseCurrentActionMenu();

        if (PlayerController.Local.PlayerHands.RightHandItem == null) return;

        this.rightHandActionMenu.Setup(PlayerController.Local.PlayerHands.RightHandItem.GetActions().ToList());

        this.currentActionMenu = this.rightHandActionMenu;
    }

    public void CloseCurrentActionMenu(bool instantly = false) {
        if (!this.currentActionMenu) return;

        this.currentActionMenu.Hide(instantly);
    }

    public void UpdateUI() {
        if (PlayerController.Local.PlayerHands.LeftHandItem) {
            this.leftHandItemIcon.sprite = PlayerController.Local.PlayerHands.LeftHandItem.Configuration.Icon;
            this.leftHandItemIcon.gameObject.SetActive(true);
        } else {
            this.leftHandItemIcon.gameObject.SetActive(false);
        }

        if (PlayerController.Local.PlayerHands.RightHandItem) {
            this.rightHandItemIcon.sprite = PlayerController.Local.PlayerHands.RightHandItem.Configuration.Icon;
            this.rightHandItemIcon.gameObject.SetActive(true);
        } else {
            this.rightHandItemIcon.gameObject.SetActive(false);
        }

        this.CloseCurrentActionMenu(true);
    }
}