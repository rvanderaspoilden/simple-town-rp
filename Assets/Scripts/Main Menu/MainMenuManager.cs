using System;
using DG.Tweening;
using Sim.Entities;
using Sim.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sim {
    public class MainMenuManager : MonoBehaviour {
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

        [Header("Apartment creation settings")]
        [SerializeField]
        private RectTransform apartmentCreationPanel;

        [SerializeField]
        private Button createApartmentButton;

        private AudioSource _audioSource;

        private string selectedPreset;

        private void Awake() {
            this._audioSource = GetComponent<AudioSource>();
        }

        private void Start() {
            this.HideApartmentCreationPanel();
            
            this.entranceDateField.text = CommonUtils.GetDate();
            this.entranceDateField.readOnly = true;

            this.bufferImg.gameObject.SetActive(false);

            this.firstNameInputField.Select();

            CheckValidity();
        }

        private void OnEnable() {
            ApiManager.OnCharacterCreated += OnCharacterCreated;
            ApiManager.OnCharacterCreationFailed += OnCharacterCreationFailed;
            ApiManager.OnApartmentAssigned += OnApartmentAssigned;
            ApiManager.OnApartmentAssignmentFailed += OnApartmentAssignmentFailed;
        }

        private void OnDisable() {
            ApiManager.OnCharacterCreated -= OnCharacterCreated;
            ApiManager.OnCharacterCreationFailed -= OnCharacterCreationFailed;
            ApiManager.OnApartmentAssigned -= OnApartmentAssigned;
            ApiManager.OnApartmentAssignmentFailed -= OnApartmentAssignmentFailed;
        }

        private void Update() {
            if (Input.GetKeyDown(KeyCode.Tab)) {
                Selectable next = EventSystem.current.currentSelectedGameObject
                    .GetComponent<Selectable>()
                    .FindSelectableOnDown();

                if (next) next.Select();
            }
        }

        public void SetPreset(string presetName) {
            this.selectedPreset = presetName;
            this.createApartmentButton.interactable = true;
        }

        public void CreateApartment() {
            this.createApartmentButton.gameObject.SetActive(false);
            ApiManager.instance.AssignApartment(new AssignApartmentRequest(NetworkManager.Instance.CharacterData.Id, this.selectedPreset));
        }

        public void CreateCharacter() {
            this.joinButton.gameObject.SetActive(false);
            ApiManager.instance.CreateCharacter(new CharacterCreationRequest(firstNameInputField.text, lastNameInputField.text, originCountryInputField.text));
        }

        public void CheckValidity() {
            this.joinButton.interactable = firstNameInputField.text != string.Empty &&
                                           lastNameInputField.text != string.Empty &&
                                           originCountryInputField.text != string.Empty;
        }

        private void OnCharacterCreated(CharacterData characterData) {
            NetworkManager.Instance.CharacterData = characterData;
            this.bufferImg.gameObject.SetActive(true);
            this._audioSource.PlayOneShot(this.bufferSound);
            Invoke(nameof(ShowApartmentCreationPanel), 2f);
        }

        private void OnCharacterCreationFailed(string err) {
            this.joinButton.gameObject.SetActive(true);
        }

        private void OnApartmentAssigned(Home home) {
            NetworkManager.Instance.TenantHome = home;
            Debug.Log("Apartment assigned !");
        }

        private void OnApartmentAssignmentFailed(string err) {
            this.createApartmentButton.gameObject.SetActive(true);
        }

        private void ShowApartmentCreationPanel() {
            this.apartmentCreationPanel.DOSizeDelta(Vector2.zero, .5f);
            this.apartmentCreationPanel.DOAnchorPos(Vector2.zero, .5f);
            this.createApartmentButton.interactable = false;
        }

        private void HideApartmentCreationPanel() {
            this.apartmentCreationPanel.sizeDelta = new Vector2(-Screen.width, 0);
            this.apartmentCreationPanel.anchoredPosition = this.apartmentCreationPanel.sizeDelta / 2;
        }
    }
}