using Mirror;
using UnityEngine;

public class FishController : NetworkBehaviour {
    [Header("Settings")]
    [SerializeField]
    private float minSpeed;

    [SerializeField]
    private float maxSpeed;

    private float _speed;

    public override void OnStartClient() {
        base.OnStartClient();
        
        if(isClientOnly) Destroy(this);
    }

    public override void OnStartServer() {
        this.RandomizeRotation();
        this.RandomizeSpeed();
    }

    private void Update() {
        if (!isServer) return;

        this.transform.Translate(Vector3.forward * Time.deltaTime * this._speed);

        if (Physics.Raycast(this.transform.position, this.transform.forward, 2f)) {
            this.RandomizeRotation();
            this.RandomizeSpeed();
        }
    }

    private void RandomizeRotation() {
        this.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
    }

    private void RandomizeSpeed() {
        this._speed = Random.Range(this.minSpeed, this.maxSpeed);
    }
}