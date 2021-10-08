using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ButtonWithRedirectUrl : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler {

    [Header("Settings")]
    [SerializeField]
    private string urlToRedirect;
    
    private Image _image;

    private void Awake() {
        this._image = GetComponent<Image>();
    }

    public void OnPointerEnter(PointerEventData eventData) {
        this._image.DOFade(1, .3f);
    }

    public void OnPointerExit(PointerEventData eventData) {
        this._image.DOFade(.6f, .3f);
    }

    public void OnPointerClick(PointerEventData eventData) {
        Application.OpenURL(this.urlToRedirect);
    }
}
