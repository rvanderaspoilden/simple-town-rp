using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sim.UI {
    public class PropsContentUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler {
        [Header("Settings")]
        [SerializeField]
        private List<TextMeshProUGUI> itemSlots;

        [SerializeField]
        private Image propsImage;

        [Header("Debug")]
        [SerializeField]
        private string[] items;

        [SerializeField]
        private int cursorIdx;

        private bool isHover;

        public delegate void SelectEvent(int itemIdx);

        public static event SelectEvent OnSelect;


        public void OnPointerEnter(PointerEventData eventData) {
            this.isHover = true;
        }

        public void OnPointerExit(PointerEventData eventData) {
            this.isHover = false;
        }

        private void Update() {
            if (isHover) {
                float scrollValue = Input.GetAxisRaw("Mouse ScrollWheel");

                if (scrollValue > 0) {
                    this.DecrementCursorIdx(1);
                } else if (scrollValue < 0) {
                    this.IncrementCursorIdx(1);
                }
            }
        }

        public void Setup(string[] newItems) {
            this.items = newItems;

            this.SetCursorIdx(0);
        }

        public void Select() {
            OnSelect?.Invoke(this.cursorIdx);
        }

        public void IncrementCursorIdx(int valueToIncrement) {
            this.SetCursorIdx(this.cursorIdx + valueToIncrement);
        }

        public void DecrementCursorIdx(int valueToDecrement) {
            this.SetCursorIdx(this.cursorIdx - valueToDecrement);
        }

        private void SetCursorIdx(int idx) {
            if (idx < 0) {
                this.cursorIdx = 0;
            } else if (idx >= this.items.Length) {
                this.cursorIdx = this.items.Length - 1;
            } else {
                this.cursorIdx = idx;
            }

            this.UpdateUI();
        }

        private void UpdateUI() {
            if (this.items == null || this.items.Length == 0) {
                this.CleanUp();
                this.itemSlots[2].text = "Nothing...";
                return;
            }

            int currentItemSlot = 0;
            for (int i = this.cursorIdx - 2; i <= this.cursorIdx + 2; i++) {
                if (i < 0 || i >= this.items.Length) {
                    this.itemSlots[currentItemSlot].text = string.Empty;
                } else {
                    this.itemSlots[currentItemSlot].text = this.items[i];
                }

                currentItemSlot++;
            }
        }

        private void CleanUp() {
            foreach (var slot in this.itemSlots) {
                slot.text = string.Empty;
            }
        }
    }
}