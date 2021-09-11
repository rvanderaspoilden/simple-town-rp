using UnityEngine;

public class TeleportPosition : MonoBehaviour {
    [Header("Settings")]
    [SerializeField]
    private string displayName;

    public Vector3 GetPosition() => this.transform.position;

    public string DisplayName => displayName;
}