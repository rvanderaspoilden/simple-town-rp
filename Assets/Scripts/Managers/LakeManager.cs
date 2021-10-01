using Mirror;
using UnityEngine;

public class LakeManager : NetworkBehaviour {
    [Header("Settings")]
    [SerializeField]
    private Transform[] fishSpawnPoints;

    [SerializeField]
    private FishController fishPrefab;

    public override void OnStartServer() {
        base.OnStartServer();

        foreach (Transform spawnPoint in this.fishSpawnPoints) {
            FishController fish = Instantiate(this.fishPrefab, spawnPoint.position, Quaternion.identity);
            NetworkServer.Spawn(fish.gameObject);
        }
    }
}