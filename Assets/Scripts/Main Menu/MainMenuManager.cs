using System.Collections.Generic;
using DG.Tweening;
using Sim.Entities;
using UnityEngine;
using UnityEngine.UI;

namespace Sim {
    public class MainMenuManager : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private CharacterCreationManager characterCreationManager;

        [SerializeField]
        private ApartmentCreationManager apartmentCreationManager;

        [SerializeField]
        private GameObject mainMenuPanel;

        [SerializeField]
        private CanvasGroup canvasGroup;

        private void Awake() {
            this.ShowCanvas(true);
            this.characterCreationManager.gameObject.SetActive(false);
            this.apartmentCreationManager.HideApartmentCreationPanel();
            this.mainMenuPanel.SetActive(false);
        }

        private void Start() {
            LoadingManager.Instance.Show(true);
            Invoke(nameof(RetrieveCharacters), 2f);
        }

        private void RetrieveCharacters() {
            ApiManager.Instance.RetrieveCharacters();
        }

        private void OnEnable() {
            ApiManager.OnCharacterRetrieved += OnCharacterRetrieved;
            ApiManager.OnHomesRetrieved += OnHomesRetrieved;
            ApiManager.OnCharacterCreated += OnCharacterCreated;
            ApiManager.OnApartmentAssigned += OnApartmentAssigned;
        }

        private void OnDisable() {
            ApiManager.OnCharacterRetrieved -= OnCharacterRetrieved;
            ApiManager.OnHomesRetrieved -= OnHomesRetrieved;
            ApiManager.OnCharacterCreated -= OnCharacterCreated;
            ApiManager.OnApartmentAssigned -= OnApartmentAssigned;
        }

        public void Play() {
            NetworkManager.Instance.Play();
        }

        private void OnCharacterRetrieved(CharacterData characterData) {
            if (characterData != null) {
                Debug.Log("Character retrieved");
                NetworkManager.Instance.CharacterData = characterData;

                ApiManager.Instance.RetrieveHomesByCharacter(characterData);
            } else {
                Debug.Log("No Character found");
                this.canvasGroup.alpha = 0;
                this.canvasGroup.GetComponent<GraphicRaycaster>().enabled = false;
                this.characterCreationManager.gameObject.SetActive(true);
                this.mainMenuPanel.SetActive(false);
                LoadingManager.Instance.Hide();
            }
        }

        private void OnHomesRetrieved(List<Home> homes) {
            this.ShowCanvas(true);

            if (homes != null && homes.Count > 0) {
                Debug.Log("Homes retrieved !");
                NetworkManager.Instance.CharacterHomes = homes;
                
                this.mainMenuPanel.SetActive(true);
            } else {
                Debug.Log("Homes not found");
                this.apartmentCreationManager.ShowApartmentCreationPanel();
            }

            LoadingManager.Instance.Hide();
        }

        private void OnCharacterCreated(CharacterData characterData) {
            NetworkManager.Instance.CharacterData = characterData;

            this.apartmentCreationManager.ShowApartmentCreationPanel();
            Invoke(nameof(FadeIn), 2f);
        }

        private void OnApartmentAssigned(Home home) {
            NetworkManager.Instance.CharacterHomes = new List<Home>() {home};
            Debug.Log("Apartment assigned !");
            this.apartmentCreationManager.HideApartmentCreationPanel();
            this.mainMenuPanel.SetActive(true);
        }

        public void FadeIn() {
            this.ShowCanvas();
        }
        
        public void ShowCanvas(bool instant = false) {
            this.canvasGroup.GetComponent<GraphicRaycaster>().enabled = true;

            if (instant) {
                this.canvasGroup.alpha = 1;
            } else {
                this.canvasGroup.DOFade(1, .3f);
            }
        }
    }
}