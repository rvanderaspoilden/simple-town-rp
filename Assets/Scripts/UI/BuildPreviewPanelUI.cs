using System;
using Sim.Building;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.UI {
    public class BuildPreviewPanelUI : MonoBehaviour {
        [Header("Settings")]
        [SerializeField] private Button validationBtn;

        [SerializeField] private Toggle toggleHideProps;
        [SerializeField] private Toggle toggleHideWalls;

        public delegate void OnValidateEvent();

        public static event OnValidateEvent OnValidate;
        
        public delegate void OnCanceledEvent();

        public static event OnCanceledEvent OnCanceled;
        
        public delegate void OnToggleHidePropsEvent(bool hide);

        public static event OnToggleHidePropsEvent OnToggleHideProps;
        
        public delegate void OnToggleHideWallsEvent(bool hide);

        public static event OnToggleHideWallsEvent OnToggleHideWalls;

        private void OnEnable() {
            BuildPreview.OnPlaceableStateChanged += this.SetValidateButtonInteractable;
        }

        private void OnDisable() {
            BuildPreview.OnPlaceableStateChanged -= this.SetValidateButtonInteractable;
        }

        public void Validate() {
            this.toggleHideProps.isOn = false;
            this.toggleHideWalls.isOn = false;

            OnValidate?.Invoke();
        }

        public void Cancel() {
            this.toggleHideProps.isOn = false;
            this.toggleHideWalls.isOn = false;

            OnCanceled?.Invoke();
        }

        public void ToggleHideProps(bool hide) {
            OnToggleHideProps?.Invoke(hide);
        }

        public void ToggleHideWalls(bool hide) {
            OnToggleHideWalls?.Invoke(hide);
        }

        private void SetValidateButtonInteractable(bool state) {
            this.validationBtn.interactable = state;
        }
    }
}
