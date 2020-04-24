using UnityEngine;

public class VirtualCameraFollow : MonoBehaviour {
    [SerializeField] private Transform target;
    
    void Update()
    {
        if (this.target) {
            this.transform.position = this.target.position;
        }
    }

    public void SetTarget(Transform target) {
        this.target = target;
    }

    public Transform GetTarget() {
        return this.target;
    }
}
