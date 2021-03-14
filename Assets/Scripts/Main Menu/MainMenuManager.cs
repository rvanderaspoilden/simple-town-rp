using System.Collections.Generic;
using Sim.Entities;
using UnityEngine;

namespace Sim {
    public class MainMenuManager : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private CharacterCreationManager characterCreationManager;

        [SerializeField]
        private ApartmentCreationManager apartmentCreationManager;

        [SerializeField]
        private GameObject mainMenuPanel;

        private void Awake() {
            this.characterCreationManager.gameObject.SetActive(false);
        }

        private void Start() {
            LoadingManager.Instance.Show(true);
            Invoke(nameof(RetrieveCharacters), 2f);
        }

        private void RetrieveCharacters() {
            ApiManager.instance.RetrieveCharacters();
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

                ApiManager.instance.RetrieveHomesByCharacter(characterData);
            } else {
                Debug.Log("No Character found");
                this.characterCreationManager.gameObject.SetActive(true);
                this.mainMenuPanel.SetActive(false);
                LoadingManager.Instance.Hide();
            }
        }

        private void OnHomesRetrieved(List<Home> homes) {
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

            this.apartmentCreationManager.Invoke(nameof(ApartmentCreationManager.ShowApartmentCreationPanel), 2f);
        }

        private void OnApartmentAssigned(Home home) {
            NetworkManager.Instance.CharacterHomes = new List<Home>() {home};
            Debug.Log("Apartment assigned !");
            this.mainMenuPanel.SetActive(true);
            this.apartmentCreationManager.HideApartmentCreationPanel();
        }
    }
}