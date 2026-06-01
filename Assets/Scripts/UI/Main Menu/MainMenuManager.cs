using System.Collections.Generic;
using Mirror;
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

        [SerializeField]
        private GameObject aboutText;
        
        private void Awake() {
            this.characterCreationManager.Hide();
            this.apartmentCreationManager.Hide();
            this.mainMenuPanel.SetActive(false);
            this.aboutText.SetActive(false);
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
            LoadingManager.Instance.Show();
            
            ((SimpleTownNetwork) NetworkManager.singleton).Invoke(nameof(NetworkManager.StartClient), 1f);
        }
        
        private void OnCharacterRetrieved(CharacterData characterData) {
            if (characterData != null) {
                Debug.Log("Character retrieved");
                ((SimpleTownNetwork) NetworkManager.singleton).CharacterData = characterData;

                ApiManager.Instance.RetrieveHomesByCharacter(characterData);
            } else {
                Debug.Log("No Character found");
                this.characterCreationManager.Show();
                LoadingManager.Instance.Hide();
            }
        }

        private void OnHomesRetrieved(List<Home> homes) {
            if (homes != null && homes.Count > 0) {
                Debug.Log("Homes retrieved !");
                ((SimpleTownNetwork) NetworkManager.singleton).CharacterHomes = homes;
            } else {
                // Existing character with no home (e.g. evicted): homelessness is a
                // valid state — let them Play and spawn in the street. The server
                // city-spawns any connection without a home. (New characters still
                // get apartment onboarding via OnCharacterCreated.)
                Debug.Log("Homes not found — playing homeless (street spawn)");
                ((SimpleTownNetwork) NetworkManager.singleton).CharacterHomes = new List<Home>();
            }

            this.mainMenuPanel.SetActive(true);
            LoadingManager.Instance.Hide();
        }

        private void OnCharacterCreated(CharacterData characterData) {
            ((SimpleTownNetwork) NetworkManager.singleton).CharacterData = characterData;

            this.characterCreationManager.Invoke(nameof(CharacterCreationManager.Hide), 2f);
            this.apartmentCreationManager.Invoke(nameof(ApartmentCreationManager.Show), 2f);
        }

        private void OnApartmentAssigned(Home home) {
            Debug.Log("Apartment assigned !");
            ((SimpleTownNetwork) NetworkManager.singleton).CharacterHomes = new List<Home>() {home};
            this.apartmentCreationManager.Hide();
            this.mainMenuPanel.SetActive(true);
        }

        public void Quit() {
            Application.Quit();
        }

        public void ShowAbout() {
            this.aboutText.SetActive(true);
        }
    }
}