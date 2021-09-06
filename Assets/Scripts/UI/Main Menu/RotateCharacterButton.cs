using Sim;
using UnityEngine;
using UnityEngine.EventSystems;

public class RotateCharacterButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler {
    [Header("Settings")]
    [SerializeField]
    private int direction = 1;

    public void OnPointerDown(PointerEventData eventData) {
        CharacterCreationManager.Instance.CharacterRotating = direction == 1 ? RotatingState.RIGHT : RotatingState.LEFT;
    }

    public void OnPointerUp(PointerEventData eventData) {
        CharacterCreationManager.Instance.CharacterRotating = RotatingState.NONE;
    }
}