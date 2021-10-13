using System;
using Sim;
using UnityEngine;
using UnityEngine.UI;

public class InventoryUI : MonoBehaviour {
    [Header("Settings")]
    [SerializeField]
    private Image leftHandItemIcon;

    [SerializeField]
    private Image rightHandItemIcon;

    private void OnEnable() {
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

    private void Update() {
        if (Input.GetKeyDown(KeyCode.Escape)) {
            HUDManager.Instance.CloseInventory();
        }
    }
}