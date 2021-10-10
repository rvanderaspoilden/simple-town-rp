using System.Collections.Generic;
using Sim;
using UnityEngine;

public class Roof : MonoBehaviour {
    [Header("Settings")]
    [SerializeField]
    private List<MeshRenderer> renderersToHide;

    [SerializeField]
    private GameObject preventClickChild;

    private bool playerInside;

    private bool cameraInside;

    private void Awake() {
        this.renderersToHide.ForEach(x => x.material = new Material(x.material));
        this.preventClickChild.SetActive(true);
    }

    private void OnTriggerStay(Collider other) {
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
        this.renderersToHide.ForEach(x =>  x.enabled = true);
        this.preventClickChild.SetActive(true);
    }

    private void Hide() {
        this.renderersToHide.ForEach(x => x.enabled = false);
        this.preventClickChild.SetActive(false);
    }
}