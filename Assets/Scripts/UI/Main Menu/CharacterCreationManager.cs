using System;
using System.Collections.Generic;
using Sim.Entities;
using Sim.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim {
    public class CharacterCreationManager : MonoBehaviour {
        [Header("Character creation settings")]
        [SerializeField]
        private RectTransform characterCreationPanel;

        [SerializeField]
        private TMP_InputField firstNameInputField;

        [SerializeField]
        private TMP_InputField lastNameInputField;

        [SerializeField]
        private TMP_InputField originCountryInputField;

        [SerializeField]
        private TMP_InputField entranceDateField;

        [SerializeField]
        private Button joinButton;

        [SerializeField]
        private Image bufferImg;

        [SerializeField]
        private AudioClip bufferSound;

        [SerializeField]
        private AudioSource audioSource;

        [SerializeField]
        private CharacterStyleSetup characterStyleSetup;

        [SerializeField]
        private List<CharacterPartButton> characterPartButtons;
        
        [SerializeField]
        private Color skinColorLimitMin;
    
        [SerializeField]
        private Color skinColorLimitMax;
        
        private CharacterPartType characterPartSelected;

        public static CharacterCreationManager Instance;

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(this);
            } else {
                Instance = this;
                this.entranceDateField.text = CommonUtils.GetDate();
                this.entranceDateField.readOnly = true;
            }
        }
        
        private void OnEnable() {
            ApiManager.OnCharacterCreated += OnCharacterCreated;
            ApiManager.OnCharacterCreationFailed += OnCharacterCreationFailed;
        }

        private void OnDisable() {
            ApiManager.OnCharacterCreated -= OnCharacterCreated;
            ApiManager.OnCharacterCreationFailed -= OnCharacterCreationFailed;
        }

        public void SkinSliderChanged(float value) {
            Color skinColorSubstract = this.skinColorLimitMax - this.skinColorLimitMin;

            this.characterStyleSetup.ApplySkinColor(new Color {
                r = (this.skinColorLimitMin.r + (skinColorSubstract.r * value)),
                g = (this.skinColorLimitMin.g + (skinColorSubstract.g * value)),
                b = (this.skinColorLimitMin.b + (skinColorSubstract.b * value))
            });
        }

        public void CreateCharacter() {
            this.joinButton.gameObject.SetActive(false);

            CharacterCreationRequest request = new CharacterCreationRequest {
                firstname = firstNameInputField.text,
                lastname = lastNameInputField.text,
                gender = Gender.MALE,
                style = this.characterStyleSetup.GetStyle(),
                entranceDate = CommonUtils.GetDate(),
                originCountry = originCountryInputField.text
            };

            ApiManager.Instance.CreateCharacter(request);
        }

        public void CheckValidity() {
            this.joinButton.interactable = firstNameInputField.text != string.Empty &&
                                           lastNameInputField.text != string.Empty &&
                                           originCountryInputField.text != string.Empty;
        }

        private void OnCharacterCreated(CharacterData characterData) {
            this.bufferImg.gameObject.SetActive(true);
            this.audioSource.PlayOneShot(this.bufferSound);
        }

        private void OnCharacterCreationFailed(string err) {
            this.joinButton.gameObject.SetActive(true);
        }

        public void Show() {
            this.characterCreationPanel.gameObject.SetActive(true);

            this.bufferImg.gameObject.SetActive(false);

            this.firstNameInputField.Select();

            CheckValidity();

            this.SetCurrentCharacterPart(CharacterPartType.HAIR);
        }

        public void Hide() {
            this.characterCreationPanel.gameObject.SetActive(false);
        }

        public void SelectRight(string characterPart) {
            CharacterPartType partType = CharacterStyleSetup.GetCharacterPartType(characterPart);
            this.characterStyleSetup.SelectPart(partType, this.characterStyleSetup.GetCurrentPartIdx(partType) + 1);
            
            this.SetCurrentCharacterPart(partType);
        }

        public void SelectLeft(string characterPart) {
            CharacterPartType partType = CharacterStyleSetup.GetCharacterPartType(characterPart);
            this.characterStyleSetup.SelectPart(partType, this.characterStyleSetup.GetCurrentPartIdx(partType) - 1);
            
            this.SetCurrentCharacterPart(partType);
        }

        public void SetColor(Color color) {
            this.characterStyleSetup.ApplyColor(this.characterPartSelected, color);
        }

        public void SetCurrentCharacterPart(CharacterPartType partType) {
            this.characterPartSelected = partType;

            this.characterPartButtons.ForEach(x => x.SetActive(x.PartType == partType));
        }
    }
}