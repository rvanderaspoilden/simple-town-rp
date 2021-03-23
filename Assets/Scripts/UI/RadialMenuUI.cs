using System;
using System.Collections.Generic;
using DG.Tweening;
using Sim.Building;
using UnityEngine;
using UnityEngine.UI;
using Action = Sim.Interactables.Action;

namespace Sim.UI {
    public class RadialMenuUI : MonoBehaviour {
        [Header("Settings")]
        [SerializeField]
        private Button radialMenuButtonPrefab;

        [SerializeField]
        private float radius;

        [Header("Only for debug")]
        [SerializeField]
        private List<Button> buttonChoices;

        private Props currentProps;

        private void Awake() {
            this.buttonChoices = new List<Button>();
        }

        private void Update() {
            if (this.currentProps) {
                this.Center();
            }
        }

        public void Center() {
            float radiansOfSeparation = (Mathf.PI * 2) / this.buttonChoices.Count;

            for (int i = 0; i < this.buttonChoices.Count; i++) {
                Button button = this.buttonChoices[i];

                button.transform.position = this.GetPosition(this.currentProps);

                if (this.buttonChoices.Count > 1) {
                    RectTransform rectTransform = button.GetComponent<RectTransform>();

                    float x = rectTransform.anchoredPosition.x + Mathf.Cos(radiansOfSeparation * i) * this.radius;
                    float y = rectTransform.anchoredPosition.y + Mathf.Sin(radiansOfSeparation * i) * this.radius;

                    rectTransform.anchoredPosition = new Vector2(x, y);
                }
            }
        }

        private Vector3 GetPosition(Props target) {
            Collider propsCollider = target.GetComponent<Collider>();
            Vector3 position = Input.mousePosition;

            if (propsCollider) {
                position = propsCollider.bounds.center;
            }

            return CameraManager.Instance.Camera.WorldToScreenPoint(position);
        }

        public void Setup(Props interactedProp) {
            this.currentProps = interactedProp;

            if (!this.currentProps) return;

            // clear view
            this.buttonChoices.ForEach(button => button.onClick.RemoveAllListeners());
            this.buttonChoices.Clear();

            foreach (Transform child in this.transform) {
                Destroy(child.gameObject);
            }

            Action[] actions = interactedProp.GetActions();

            for (int i = 0; i < actions.Length; i++) {
                Action action = actions[i];
                Button button = Instantiate(this.radialMenuButtonPrefab, this.transform);
                button.interactable = !action.IsLocked();
                button.onClick.AddListener(() => {
                    interactedProp.DoAction(action);
                    this.gameObject.SetActive(false); // close context menu after action triggered
                });

                button.GetComponent<Image>().sprite = action.ActionIcon;

                RectTransform rectTransform = button.GetComponent<RectTransform>();

                rectTransform.localScale = Vector2.zero;
                rectTransform.DOScale(Vector3.one, .3f).SetEase(Ease.OutQuad).SetDelay(0.05F * i);
                
                this.buttonChoices.Add(button);
            }
            
            Center();
        }
    }
}