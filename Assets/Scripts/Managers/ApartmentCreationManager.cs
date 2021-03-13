using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
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


        private void Awake() {
            this.HideApartmentCreationPanel();
        }
        
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
            ApiManager.instance.AssignApartment(new AssignApartmentRequest(NetworkManager.Instance.CharacterData.Id, this.selectedPreset));
        }
        
        private void OnApartmentAssigned(Home home) {
            // TODO: use to show animation
        }

        private void OnApartmentAssignmentFailed(string err) {
            this.createApartmentButton.gameObject.SetActive(true);
        }

        public void ShowApartmentCreationPanel() {
            this.apartmentCreationPanel.DOSizeDelta(Vector2.zero, .5f);
            this.apartmentCreationPanel.DOAnchorPos(Vector2.zero, .5f);
            this.createApartmentButton.interactable = false;
        }

        public void HideApartmentCreationPanel() {
            this.apartmentCreationPanel.sizeDelta = new Vector2(-Screen.width, 0);
            this.apartmentCreationPanel.anchoredPosition = this.apartmentCreationPanel.sizeDelta / 2;
        }
    }
}