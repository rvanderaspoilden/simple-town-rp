using System.Collections.Generic;
using Sim;
using UnityEngine;
using UnityEngine.Rendering;

public class Roof : MonoBehaviour {
    [Header("Settings")]
    [SerializeField]
    private List<MeshRenderer> renderersToHide;

    private bool playerInside;

    private bool cameraInside;

    private void OnTriggerEnter(Collider other) {
        if (PlayerController.Local && other.CompareTag("Player") && other.gameObject == PlayerController.Local.gameObject) {
            playerInside = true;
        } else if (other.CompareTag("MainCamera")) {
            cameraInside = true;
        }

        if (playerInside || cameraInside) {
            Hide();
        }
    }

    private void OnTriggerExit(Collider other) {
        if (PlayerController.Local && other.CompareTag("Player") && other.gameObject == PlayerController.Local.gameObject) {
            playerInside = false;
        } else if (other.CompareTag("MainCamera")) {
            cameraInside = false;
        }

        if (!playerInside && !cameraInside) {
            this.Show();
        }
    }

    private void Show() {
        this.renderersToHide.ForEach(x => x.enabled = true);
    }

    private void Hide() {
        this.renderersToHide.ForEach(x => x.enabled = false);
    }
}