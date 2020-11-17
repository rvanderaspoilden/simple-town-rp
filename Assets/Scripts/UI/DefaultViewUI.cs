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

        private bool phoneOpened;
        
        private Vector2 firstPressPos;
        private Vector2 secondPressPos;
        private Vector2 currentSwipe;
        private float defaultPhoneAnchorPosY;

        private void Awake() {
            this.defaultPhoneAnchorPosY = phone.anchoredPosition.y;
        }

        private void Start() {
            this.characterInfoPanelUI.Setup(NetworkManager.Instance.Personnage);
            this.SetLocationText(PhotonNetwork.CurrentRoom.Name);
        }

        private void SetLocationText(string value) {
            foreach (TextMeshProUGUI tmpPro in this.locationText.GetComponentsInChildren<TextMeshProUGUI>()) {
                tmpPro.text = value;
            }
        }

        private void Update() {
            this.Swipe();
        }

        public void OpenPhone() {
            if (!this.phoneOpened) {
                this.phone.DOAnchorPosY(0, 0.5f).SetEase(Ease.Flash).OnComplete(() => this.phoneOpened = true);
            }
        }

        public void ClosePhone() {
            if (this.phoneOpened) {
                this.phone.DOAnchorPosY(this.defaultPhoneAnchorPosY, 1).SetEase(Ease.OutBounce).OnComplete(() => this.phoneOpened = false);
            }
        }
        
        // TODO: it's temporary
        public void Swipe()
        {
            if(Input.GetMouseButtonDown(0))
            {
                firstPressPos = new Vector2(Input.mousePosition.x,Input.mousePosition.y);
            }
            if(Input.GetMouseButtonUp(0))
            {
                secondPressPos = new Vector2(Input.mousePosition.x,Input.mousePosition.y);
       
                currentSwipe = new Vector2(secondPressPos.x - firstPressPos.x, secondPressPos.y - firstPressPos.y);
           
                currentSwipe.Normalize();
 
                //swipe upwards
                if(currentSwipe.y > 0 && currentSwipe.x > -0.5f && currentSwipe.x < 0.5f)
                {
                    this.OpenPhone();
                }
                
                //swipe down
                if(currentSwipe.y < 0 && currentSwipe.x > -0.5f && currentSwipe.x < 0.5f)
                {
                    this.ClosePhone();
                }
            }
        }
    }
}

