using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Mirror;
using Sim.Entities;
using UnityEngine;
using UnityEngine.UI;

namespace Sim {
    public class ApartmentCreationManager : MonoBehaviour {
        
        [Header("Apartment creation settings")]
        [SerializeField]
        private RectTransform apartmentCreationPanel;

        [SerializeField]
        private Button createApartmentButton;
        
        private string selectedPreset;

        private void OnEnable() {
            ApiManager.OnApartmentAssigned += OnApartmentAssigned;
            ApiManager.OnApartmentAssignmentFailed += OnApartmentAssignmentFailed;
        }

        private void OnDisable() {
            ApiManager.OnApartmentAssigned -= OnApartmentAssigned;
            ApiManager.OnApartmentAssignmentFailed -= OnApartmentAssignmentFailed;
        }
        
        public void SetPreset(string presetName) {
            this.selectedPreset = presetName;
            this.createApartmentButton.interactable = true;
        }

        public void CreateApartment() {
            this.createApartmentButton.gameObject.SetActive(false);
            ApiManager.Instance.AssignApartment(new AssignApartmentRequest(((SimpleTownNetwork) NetworkManager.singleton).CharacterData.Id, this.selectedPreset));
        }
        
        private void OnApartmentAssigned(Home home) {
            // TODO: use to show animation
        }

        private void OnApartmentAssignmentFailed(string err) {
            this.createApartmentButton.gameObject.SetActive(true);
        }

        public void Show() {
            this.apartmentCreationPanel.gameObject.SetActive(true);
            this.createApartmentButton.interactable = false;
        }

        public void Hide() {
            this.apartmentCreationPanel.gameObject.SetActive(false);
        }
    }
}