using System.Collections.Generic;
using Mirror;
using Sim.Entities;
using UnityEngine;

namespace Sim {
    public class MainMenuManager : MonoBehaviour {
        [Header("Panels")]
        [SerializeField]
        private HomePanel homePanel;

        [SerializeField]
        private CharacterCreationManager characterCreationManager;

        [SerializeField]
        private ApartmentCreationManager apartmentCreationManager;

        [SerializeField]
        private AboutPanel aboutPanel;

        [SerializeField]
        private GameObject inventoryPreviewPanel;

        [SerializeField]
        private GameObject questsPreviewPanel;

        private void Awake() {
            this.characterCreationManager.Hide();
            this.apartmentCreationManager.Hide();
            this.homePanel.Hide();
            this.aboutPanel.Hide();
            if (this.inventoryPreviewPanel != null) this.inventoryPreviewPanel.SetActive(false);
            if (this.questsPreviewPanel != null) this.questsPreviewPanel.SetActive(false);
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

        public void EnterCity() {
            SimpleTownNetwork network = (SimpleTownNetwork) NetworkManager.singleton;
            bool hasHome = network.CharacterHomes != null && network.CharacterHomes.Count > 0;

            if (!hasHome) {
                this.homePanel.Hide();
                this.apartmentCreationManager.Show();
                return;
            }

            LoadingManager.Instance.Show();
            network.Invoke(nameof(NetworkManager.StartClient), 1f);
        }

        public void OpenCharacterPanel() {
            this.characterCreationManager.Show();
        }

        public void OpenInventoryPreview() {
            if (this.inventoryPreviewPanel != null) this.inventoryPreviewPanel.SetActive(true);
        }

        public void OpenQuestsPreview() {
            if (this.questsPreviewPanel != null) this.questsPreviewPanel.SetActive(true);
        }

        public void OpenAbout() {
            this.aboutPanel.Show();
        }

        public void CloseAbout() {
            this.aboutPanel.Hide();
        }

        public void Quit() {
            Application.Quit();
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
            SimpleTownNetwork network = (SimpleTownNetwork) NetworkManager.singleton;

            if (homes != null && homes.Count > 0) {
                Debug.Log("Homes retrieved !");
                network.CharacterHomes = homes;
            } else {
                // Existing character with no home (e.g. evicted): homelessness is a
                // valid state — let them Play and spawn in the street. The server
                // city-spawns any connection without a home. (New characters still
                // get apartment onboarding via OnCharacterCreated.)
                Debug.Log("Homes not found — playing homeless (street spawn)");
                network.CharacterHomes = new List<Home>();
            }

            this.homePanel.Bind(network.CharacterData);
            this.homePanel.Show();
            LoadingManager.Instance.Hide();
        }

        private void OnCharacterCreated(CharacterData characterData) {
            ((SimpleTownNetwork) NetworkManager.singleton).CharacterData = characterData;

            this.characterCreationManager.Invoke(nameof(CharacterCreationManager.Hide), 2f);
            this.apartmentCreationManager.Invoke(nameof(ApartmentCreationManager.Show), 2f);
        }

        private void OnApartmentAssigned(Home home) {
            Debug.Log("Apartment assigned !");
            SimpleTownNetwork network = (SimpleTownNetwork) NetworkManager.singleton;
            network.CharacterHomes = new List<Home>() {home};
            this.apartmentCreationManager.Hide();

            this.homePanel.Bind(network.CharacterData);
            this.homePanel.Show();
        }
    }
}
