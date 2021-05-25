using System.Collections;
using System.Collections.Generic;
using Sim;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class GenderSelector : MonoBehaviour, IPointerClickHandler
{
    [Header("Settings")]
    [SerializeField]
    private Gender gender;

    [SerializeField]
    private Color activeColor;

    private Image image;

    private void Awake() {
        this.image = GetComponent<Image>();
    }

    public Gender Gender => gender;

    public void SetActive(bool isActive) {
        this.image.color = isActive ? this.activeColor : Color.white;
        this.transform.localScale = isActive ? Vector3.one * 1.1f : Vector3.one;
    }

    public void OnPointerClick(PointerEventData eventData) {
        CharacterCreationManager.Instance.SetGender(this.gender);
    }
}
