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

    public void Confirm() {
        if (int.TryParse(this.searchTxt.text, out var floorToGo)) {
            HUDManager.Instance.PlaySound(this.navigateButtonClickSound, .6f);
            this.teleporterBind.CmdUse(floorToGo);
            DefaultViewUI.Instance.HideElevatorUI();
            LoadingManager.Instance.Show(true);
        }
    }

    public void GoToHall() {
        HUDManager.Instance.PlaySound(this.navigateButtonClickSound, .6f);
        
        this.teleporterBind.CmdUse(0);
        DefaultViewUI.Instance.HideElevatorUI();
        LoadingManager.Instance.Show(true);
    }

    public void Abort() {
        DefaultViewUI.Instance.HideElevatorUI();
        HUDManager.Instance.StopBackgroundSound();
    }
}