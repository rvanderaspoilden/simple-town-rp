using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Building;
using Sim.Entities;
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

        [SerializeField]
        private Props linkedProps;

        private bool isHover;

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

        public void Setup(Props props) {
            this.linkedProps = props;
            this.propsImage.sprite = props.GetConfiguration().Sprite;
            
            if (this.linkedProps.GetType() == typeof(Dispenser)) {
                this.items = ((DispenserConfiguration) this.linkedProps.GetConfiguration()).ItemsToSell.Select(x => x.DisplayWithPrice()).ToArray();
            } else if (this.linkedProps.GetType() == typeof(DeliveryBox)) {
                this.items = ((DeliveryBox) this.linkedProps).Deliveries.Select(x => x.DisplayName()).ToArray();
            }
            
            this.SetCursorIdx(0);
        }

        public void Select() {
            if (this.linkedProps.GetType() == typeof(Dispenser)) {
                ItemConfig itemConfig = ((DispenserConfiguration) this.linkedProps.GetConfiguration()).ItemsToSell[this.cursorIdx].item;
                ((Dispenser)this.linkedProps).BuyItem(itemConfig);
            } else if (this.linkedProps.GetType() == typeof(DeliveryBox)) {
                Delivery delivery = ((DeliveryBox) this.linkedProps).Deliveries[this.cursorIdx];
                ((DeliveryBox)this.linkedProps).OpenDelivery(delivery);
            }
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