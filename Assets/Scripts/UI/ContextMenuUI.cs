using System;
using System.Collections.Generic;
using Sim.Building;
using Sim.Interactables;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Action = Sim.Interactables.Action;

namespace Sim.UI {
    public class ContextMenuUI : MonoBehaviour {
        [Header("Settings")]
        [SerializeField] private Button choiceButtonPrefab;

        [SerializeField]
        private float circleRadius;

        [Header("Only for debug")]
        [SerializeField] private List<Button> buttonChoices;

        [SerializeField]
        private RectTransform rectTransform;

        private const float MAX_ANGLE = 360f;

        private void Awake() {
            this.buttonChoices = new List<Button>();
            this.rectTransform = GetComponent<RectTransform>();
        }

        public void Setup(Props interactedProp) {
            // clear view
            this.buttonChoices.ForEach(button => button.onClick.RemoveAllListeners());
            this.buttonChoices.Clear();

            foreach (Transform child in this.transform) {
                Destroy(child.gameObject);
            }

            for (int i = 0; i < interactedProp.GetActions().Length; i++) {
                Action action = interactedProp.GetActions()[i];
                Button button = Instantiate(this.choiceButtonPrefab, this.transform);
                
                button.transform.position = PointOnCircle(this.circleRadius, (MAX_ANGLE / interactedProp.GetActions().Length) * i, this.rectTransform.anchoredPosition);
                button.interactable = !action.IsLocked();
                button.GetComponentInChildren<TextMeshProUGUI>().text = action.GetActionLabel();
                button.onClick.AddListener(() => {
                    interactedProp.DoAction(action);
                    this.gameObject.SetActive(false); // close context menu after action triggered
                });
                this.buttonChoices.Add(button);
            }
        }
        
        public static Vector2 PointOnCircle(float radius, float angleInDegrees, Vector2 origin)
        {
            // Convert from degrees to radians via multiplication by PI/180        
            float x = (float)(radius * Math.Cos(angleInDegrees * Math.PI / 180F)) + origin.x;
            float y = (float)(radius * Math.Sin(angleInDegrees * Math.PI / 180F)) + origin.y;

            return new Vector2(x, y);
        }
    }
}
