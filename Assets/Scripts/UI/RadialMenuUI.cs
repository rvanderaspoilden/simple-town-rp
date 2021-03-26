using System;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using Sim.Building;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Action = Sim.Interactables.Action;

namespace Sim.UI {
    public class RadialMenuUI : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private RadialMenuButton radialMenuButtonPrefab;

        [SerializeField]
        private Image radialImage;

        [SerializeField]
        private RectTransform radialRectTransform;

        [SerializeField]
        private float radius;

        [SerializeField]
        private float backgroundRadiusOffset;

        [SerializeField]
        private TextMeshProUGUI actionText;

        [Header("Only for debug")]
        [SerializeField]
        private List<RadialMenuButton> radialMenuButtons;

        private Props currentProps;

        private Collider currentPropsCollider;

        private void Awake() {
            this.radialMenuButtons = new List<RadialMenuButton>();
            this.actionText.enabled = false;
            this.radialImage.gameObject.SetActive(false);
        }

        private void Start() {
            this.radialRectTransform = this.radialImage.GetComponent<RectTransform>();
        }

        private void OnEnable() {
            RadialMenuButton.OnClicked += OnRadialButtonClicked;
            RadialMenuButton.OnHover += OnRadialButtonHover;
            RadialMenuButton.OnExit += OnRadialButtonExit;
        }

        private void OnDisable() {
            RadialMenuButton.OnClicked -= OnRadialButtonClicked;
            RadialMenuButton.OnHover -= OnRadialButtonHover;
            RadialMenuButton.OnExit -= OnRadialButtonExit;
        }

        private void Update() {
            if (this.currentProps) {
                this.Center();
            }
        }

        public void Center() {
            float radiansOfSeparation = (Mathf.PI * 2) / this.radialMenuButtons.Count;

            Vector3 origin = this.GetPosition();

            if (this.radialMenuButtons.Count > 1) {
                this.radialImage.transform.position = origin;
                this.radialRectTransform.sizeDelta = new Vector2((radius + backgroundRadiusOffset) * 2f, (radius + backgroundRadiusOffset) * 2f);
            }

            for (int i = 0; i < this.radialMenuButtons.Count; i++) {
                RadialMenuButton button = this.radialMenuButtons[i];

                button.transform.position = origin;

                RectTransform buttonRectTransform = button.RectTransform;

                if (this.radialMenuButtons.Count > 1) {
                    float x = buttonRectTransform.anchoredPosition.x + Mathf.Cos(radiansOfSeparation * i) * this.radius;
                    float y = buttonRectTransform.anchoredPosition.y + Mathf.Sin(radiansOfSeparation * i) * this.radius;

                    buttonRectTransform.anchoredPosition = new Vector2(x, y);
                }
            }
        }

        private Vector3 GetPosition() {
            Vector3 position = Input.mousePosition;

            if (currentPropsCollider) {
                position = currentPropsCollider.bounds.center;
            }

            return CameraManager.Instance.Camera.WorldToScreenPoint(position);
        }

        public void Setup(Props interactedProp, bool withPriority = false) {
            this.currentProps = interactedProp;

            this.currentPropsCollider = interactedProp.GetComponent<Collider>();

            this.ClearButtons();

            this.ClearText();

            Action[] actions = interactedProp.GetActions(withPriority);

            if (withPriority && actions.Length > 1) {
                actions = new[] {actions[0]};
            }

            if (actions.Length > 1) {
                this.radialImage.gameObject.SetActive(true);

                this.radialImage.color = new Color(0, 0, 0, 0);
                this.radialImage.DOComplete();
                this.radialImage.DOColor(Color.white, .3f).SetEase(Ease.OutQuad);
            } else {
                this.radialImage.gameObject.SetActive(false);
            }


            for (int i = 0; i < actions.Length; i++) {
                Action action = actions[i];
                RadialMenuButton button = Instantiate(this.radialMenuButtonPrefab, this.transform);

                button.Setup(action);
                button.GetComponent<Image>().sprite = action.Icon;

                RectTransform rectTransform = button.GetComponent<RectTransform>();

                rectTransform.DOComplete();

                rectTransform.localScale = Vector2.zero;
                rectTransform.DOScale(Vector3.one, .3f).SetEase(Ease.OutQuad).SetDelay(0.05F * i);

                this.radialMenuButtons.Add(button);
            }
        }

        private void ClearButtons() {
            foreach (Transform child in this.transform) {
                if (child != this.radialImage.transform) {
                    Destroy(child.gameObject);
                }
            }

            this.radialMenuButtons.Clear();
        }

        private void ClearText() {
            this.actionText.text = string.Empty;
            this.actionText.enabled = false;
        }

        private void OnRadialButtonHover(Action action) {
            this.actionText.enabled = true;
            this.actionText.text = action.Label;
        }

        private void OnRadialButtonExit(Action action) {
            this.ClearText();
        }

        private void OnRadialButtonClicked(Action action) {
            this.currentProps.DoAction(action);
            this.currentProps = null;
            this.gameObject.SetActive(false);
        }
    }
}