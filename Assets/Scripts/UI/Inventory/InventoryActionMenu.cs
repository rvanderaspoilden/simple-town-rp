using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Sim.Interactables;
using UnityEngine;

public class InventoryActionMenu : MonoBehaviour {
    [Header("Settings")]
    [SerializeField]
    private Transform contentTransform;
    
    [SerializeField]
    private InventoryActionButton inventoryActionButtonPrefab;
    
    [Header("Only for debug")]
    [SerializeField]
    private List<InventoryActionButton> inventoryActionButtons;

    private CanvasGroup _canvasGroup;
    
    private void Awake() {
        this.inventoryActionButtons = new List<InventoryActionButton>();
        this._canvasGroup = GetComponent<CanvasGroup>();

        this._canvasGroup.alpha = 0;
    }

    public void Setup(List<Action> actions) {
        this.ClearButtons();
        
        actions.ForEach(action => {
            InventoryActionButton button = Instantiate(this.inventoryActionButtonPrefab, this.contentTransform);

            button.Setup(action);

            this.inventoryActionButtons.Add(button);
        });

        this._canvasGroup.DOFade(1, .3f);
    }

    public void Hide() {
        this._canvasGroup.DOFade(0, .3f);
    }
    
    private void ClearButtons() {
        foreach (InventoryActionButton child in this.inventoryActionButtons) {
            Destroy(child.gameObject);
        }

        this.inventoryActionButtons.Clear();
    }
}
