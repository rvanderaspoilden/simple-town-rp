using System;
using Sim;
using UnityEngine;
using UnityEngine.UI;

public class CharacterPartButton : MonoBehaviour {
    [Header("Settings")]
    [SerializeField]
    private CharacterPartType partType;

    private Image image;

    private Button button;

    private void Awake() {
        this.image = GetComponent<Image>();
        this.button = GetComponent<Button>();
    }

    private void OnEnable() {
        this.button.onClick.AddListener(() => CharacterCreationManager.Instance.SetCurrentCharacterPart(this.partType));
    }

    public CharacterPartType PartType => partType;

    public void SetActive(bool isActive) {
        this.image.color = new Color(this.image.color.r, this.image.color.g, this.image.color.b, isActive ? .6f : 0);
    }
}