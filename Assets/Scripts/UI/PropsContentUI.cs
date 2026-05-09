using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Building;
using Sim.Entities;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sim.UI
{
    public class PropsContentUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [Header("Settings")] [SerializeField] private List<TextMeshProUGUI> itemSlots;

        [SerializeField] private Image propsImage;

        [Header("Debug")] [SerializeField] private string[] items;

        [SerializeField] private int cursorIdx;

        [SerializeField] private PropBehaviourBase linkedPropBehaviour; // New system

        private bool isHover;

        private void OnEnable()
        {
            DispenserBehaviour.OnPurchaseResult   += OnPurchaseResult;
            DeliveryBoxBehaviour.UnPackage        += OnDeliveryUnpackage;
        }

        private void OnDisable()
        {
            DispenserBehaviour.OnPurchaseResult   -= OnPurchaseResult;
            DeliveryBoxBehaviour.UnPackage        -= OnDeliveryUnpackage;
            this.linkedPropBehaviour = null;
        }

        private void OnPurchaseResult(int itemId, bool success)
        {
            if (!success) return;
            Debug.Log("[PropsContentUI] Closing and resetting interaction state (purchase)");
            CloseAndResetInteraction();
        }

        private void OnDeliveryUnpackage(Sim.Entities.Delivery delivery)
        {
            Debug.Log("[PropsContentUI] Closing and resetting interaction state (delivery)");
            // Note: delivery flow already changes player state via OpenPackageFromDeliveryBox;
            // we just hide the UI here without forcing Idle (it would override UNPACKAGING).
            DefaultViewUI.Instance.HidePropsContentUI();
        }

        /// <summary>
        /// Hides the UI and explicitly returns the local player to Idle so the
        /// state machine can show the context menu again on the next interaction.
        /// </summary>
        private void CloseAndResetInteraction()
        {
            DefaultViewUI.Instance.HidePropsContentUI();
            PlayerController.Local?.Idle();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            this.isHover = true;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            this.isHover = false;
        }

        private void Update()
        {
            if (isHover)
            {
                float scrollValue = Input.GetAxisRaw("Mouse ScrollWheel");

                if (scrollValue > 0)
                {
                    this.DecrementCursorIdx(1);
                }
                else if (scrollValue < 0)
                {
                    this.IncrementCursorIdx(1);
                }
            }
        }


        public void Setup(PropBehaviourBase behaviour)
        {
            this.linkedPropBehaviour = behaviour;
            this.propsImage.sprite = behaviour.GetConfiguration()?.Sprite;

            // Handle specific prop types
            if (behaviour is DispenserBehaviour dispenser)
            {
                DispenserConfiguration config = dispenser.GetConfiguration();
                if (config != null)
                {
                    this.items = config.ItemsToSell.Select(x => x.DisplayWithPrice()).ToArray();
                }
            }
            else if (behaviour is DeliveryBoxBehaviour deliveryBox)
            {
                // Get deliveries from the DeliveryBoxBehaviour
                Delivery[] deliveries = deliveryBox.Deliveries;
                this.items = deliveries is { Length: > 0 }
                    ? deliveries.Select(x => x.DisplayName()).ToArray()
                    : System.Array.Empty<string>();
            }

            this.SetCursorIdx(0);
        }

        public void Select()
        {
            if (this.linkedPropBehaviour != null)
            {
                // New system
                if (this.linkedPropBehaviour is DispenserBehaviour dispenser)
                {
                    DispenserConfiguration config = dispenser.GetConfiguration();
                    if (config != null)
                    {
                        ItemConfig itemConfig = config.ItemsToSell[this.cursorIdx].item;
                        dispenser.BuyItem(itemConfig);
                    }
                }
                else if (this.linkedPropBehaviour is DeliveryBoxBehaviour deliveryBox)
                {
                    // Get the selected delivery and open it
                    Delivery[] deliveries = deliveryBox.Deliveries;
                    if (deliveries != null && this.cursorIdx >= 0 && this.cursorIdx < deliveries.Length)
                    {
                        Delivery delivery = deliveries[this.cursorIdx];
                        deliveryBox.OpenDelivery(delivery);
                    }
                }
            }
        }

        public void IncrementCursorIdx(int valueToIncrement)
        {
            this.SetCursorIdx(this.cursorIdx + valueToIncrement);
        }

        public void DecrementCursorIdx(int valueToDecrement)
        {
            this.SetCursorIdx(this.cursorIdx - valueToDecrement);
        }

        private void SetCursorIdx(int idx)
        {
            if (idx < 0)
            {
                this.cursorIdx = 0;
            }
            else if (idx >= this.items.Length)
            {
                this.cursorIdx = this.items.Length - 1;
            }
            else
            {
                this.cursorIdx = idx;
            }

            this.UpdateUI();
        }

        private void UpdateUI()
        {
            if (this.items == null || this.items.Length == 0)
            {
                this.CleanUp();
                this.itemSlots[2].text = "Nothing...";
                return;
            }

            int currentItemSlot = 0;
            for (int i = this.cursorIdx - 2; i <= this.cursorIdx + 2; i++)
            {
                if (i < 0 || i >= this.items.Length)
                {
                    this.itemSlots[currentItemSlot].text = string.Empty;
                }
                else
                {
                    this.itemSlots[currentItemSlot].text = this.items[i];
                }

                currentItemSlot++;
            }
        }

        private void CleanUp()
        {
            foreach (var slot in this.itemSlots)
            {
                slot.text = string.Empty;
            }
        }
    }
}