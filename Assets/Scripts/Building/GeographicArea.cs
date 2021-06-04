using UnityEngine;

public class GeographicArea : MonoBehaviour {
    [Header("Settings")]
    [SerializeField]
    private string locationText;

    [SerializeField]
    private int priorityOrder = 1;
    
    public string LocationText {
        get => locationText;
        set => locationText = value;
    }

    public int PriorityOrder => priorityOrder;
}