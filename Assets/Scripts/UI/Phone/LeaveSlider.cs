using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class LeaveSlider : Slider {
    [Header("Settings")]
    [SerializeField]
    private TextMeshProUGUI placeholder;

    public override void OnDrag(PointerEventData eventData) {
        base.OnDrag(eventData);
        placeholder.gameObject.SetActive(false); 
    }

    public override void OnPointerUp(PointerEventData eventData) {
        Debug.Log("Deselect");
        placeholder.gameObject.SetActive(true);
        this.ResetSlider();
    }

    private void ResetSlider() {
        Debug.Log("Reset Slider");
        this.DOComplete();
        this.DOValue(0f, .3f).SetEase(Ease.Linear);
    }
}
