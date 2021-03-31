using System;
using System.Collections;
using System.Collections.Generic;
using Sim.Entities;
using Sim.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sim {
    public class CharacterCreationManager : MonoBehaviour {
        [Header("Character creation settings")]
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

        private void Awake() {
            this.entranceDateField.text = CommonUtils.GetDate();
            this.entranceDateField.readOnly = true;

            this.bufferImg.gameObject.SetActive(false);

            this.firstNameInputField.Select();

            CheckValidity();
        }
        
        private void OnEnable() {
            ApiManager.OnCharacterCreated += OnCharacterCreated;
            ApiManager.OnCharacterCreationFailed += OnCharacterCreationFailed;
        }

        private void OnDisable() {
            ApiManager.OnCharacterCreated -= OnCharacterCreated;
            ApiManager.OnCharacterCreationFailed -= OnCharacterCreationFailed;
        }

        private void Update() {
            if (Input.GetKeyDown(KeyCode.Tab)) {
                Selectable next = EventSystem.current.currentSelectedGameObject
                    .GetComponent<Selectable>()
                    .FindSelectableOnDown();

                if (next) next.Select();
            }
        }
        
        public void CreateCharacter() {
            this.joinButton.gameObject.SetActive(false);
            ApiManager.Instance.CreateCharacter(new CharacterCreationRequest(firstNameInputField.text, lastNameInputField.text, originCountryInputField.text));
        }

        public void CheckValidity() {
            this.joinButton.interactable = firstNameInputField.text != string.Empty &&
                                           lastNameInputField.text != string.Empty &&
                                           originCountryInputField.text != string.Empty;
        }
        
        private void OnCharacterCreated(CharacterData characterData) {
            NetworkManager.Instance.CharacterData = characterData;
            this.bufferImg.gameObject.SetActive(true);
            this.audioSource.PlayOneShot(this.bufferSound);
        }

        private void OnCharacterCreationFailed(string err) {
            this.joinButton.gameObject.SetActive(true);
        }
    }
}