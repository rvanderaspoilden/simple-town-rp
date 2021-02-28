using DG.Tweening;
using Photon.Pun;
using Sim.UI;
using TMPro;
using UnityEngine;

namespace Sim {
    public class DefaultViewUI : MonoBehaviour {

        [Header("Settings")]
        [SerializeField]
        private CharacterInfoPanelUI characterInfoPanelUI;

        [SerializeField]
        private TextMeshProUGUI locationText;
        
        [SerializeField]
        private RectTransform phone;

        private void Start() {
            this.SetLocationText(PhotonNetwork.CurrentRoom.Name);
        }

        private void SetLocationText(string value) {
            foreach (TextMeshProUGUI tmpPro in this.locationText.GetComponentsInChildren<TextMeshProUGUI>()) {
                tmpPro.text = value;
            }
        }
    }
}

