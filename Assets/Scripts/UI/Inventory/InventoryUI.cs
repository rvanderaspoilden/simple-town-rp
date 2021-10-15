using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using Sim;
using Sim.Interactables;
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
    
    private void OnEnable() {
        this.UpdateUI();
    }

    private void Update() {
        if (Input.GetKeyDown(KeyCode.Escape)) {
            HUDManager.Instance.CloseInventory();
        }
    }

    public void DisplayLeftActionMenu() {
        if (PlayerController.Local.PlayerHands.LeftHandItem == null) return;

        this.leftHandActionMenu.Setup(PlayerController.Local.PlayerHands.LeftHandItem.GetActions().ToList());
    }

    public void CloseLeftActionMenu() {
        this.leftHandActionMenu.Hide();
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
    }
}