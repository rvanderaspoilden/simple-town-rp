using System;
using Sim;
using Sim.Interactables;
using TMPro;
using UnityEngine;

public class ElevatorUI : MonoBehaviour {
    [Header("Settings")]
    [SerializeField]
    private TMP_InputField searchTxt;

    [SerializeField]
    private AudioClip numberButtonClickSound;

    [SerializeField]
    private AudioClip navigateButtonClickSound;

    [SerializeField]
    private AudioClip backgroundSound;

    private Teleporter teleporterBind;

    public void Bind(Teleporter bind) {
        this.teleporterBind = bind;
        this.searchTxt.text = string.Empty;
        HUDManager.Instance.PlayBackgroundSound(this.backgroundSound, .03f);
    }

    public void AddNumber(int value) {
        HUDManager.Instance.PlaySound(this.numberButtonClickSound, 1);

        if (this.searchTxt.text.Length < 2) {
            this.searchTxt.text += value.ToString();
        }
    }

    private void OnDisable() {
        HUDManager.Instance.StopBackgroundSound();
    }

    public void Confirm() {
        if (int.TryParse(this.searchTxt.text, out var floorToGo)) {
            int originFloor = this.teleporterBind.HallController ? this.teleporterBind.HallController.FloorNumber : 0;

            if (originFloor != floorToGo) {
                HUDManager.Instance.PlaySound(this.navigateButtonClickSound, .6f);
                this.teleporterBind.CmdUse(floorToGo);
                DefaultViewUI.Instance.HideElevatorUI();
                LoadingManager.Instance.Show(true);
            } else {
                DefaultViewUI.Instance.HideElevatorUI();
            }
        }
    }

    public void GoToHall() {
        int originFloor = this.teleporterBind.HallController ? this.teleporterBind.HallController.FloorNumber : 0;

        if (originFloor != 0) {
            HUDManager.Instance.PlaySound(this.navigateButtonClickSound, .6f);
            DefaultViewUI.Instance.HideElevatorUI();
            this.teleporterBind.CmdUse(0);
        } else {
            DefaultViewUI.Instance.HideElevatorUI();
        }
    }

    public void Abort() {
        DefaultViewUI.Instance.HideElevatorUI();
    }
}