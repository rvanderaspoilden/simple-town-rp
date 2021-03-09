using System;
using Sim.Entities;
using Sim.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sim {
    public class MainMenuManager : MonoBehaviour {
        [Header("Settings")]
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

        private void Start() {
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
            ApiManager.instance.CreateCharacter(new CharacterCreationRequest(firstNameInputField.text, lastNameInputField.text, originCountryInputField.text));
            this.joinButton.gameObject.SetActive(false);
        }

        public void CheckValidity() {
            this.joinButton.interactable = firstNameInputField.text != string.Empty &&
                                           lastNameInputField.text != string.Empty &&
                                           originCountryInputField.text != string.Empty;
        }

        private void OnCharacterCreated(CharacterData characterData) {
            Debug.Log("Character created !");
            this.bufferImg.gameObject.SetActive(true);
        }

        private void OnCharacterCreationFailed(string err) {
            this.joinButton.gameObject.SetActive(true);
        }
    }
}