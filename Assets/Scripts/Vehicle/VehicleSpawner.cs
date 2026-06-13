using Mirror;
using UnityEngine;

/// <summary>
/// Spawner serveur d'un véhicule. À placer dans la scène City. Au démarrage serveur,
/// instancie le prefab véhicule (chargé depuis Resources) à sa position et le spawn
/// via Mirror. Ne fait rien côté client (NetworkServer.active == false).
///
/// Namespace global (cohérent avec VehicleController / types réseau).
/// </summary>
public class VehicleSpawner : MonoBehaviour {
    [Tooltip("Chemin Resources du prefab véhicule (sans extension).")]
    [SerializeField] private string resourcePath = "Prefabs/Vehicles/Vehicle";

    private void Start() {
        if (!NetworkServer.active) return;

        GameObject prefab = Resources.Load<GameObject>(resourcePath);
        if (prefab == null) {
            Debug.LogError($"[VehicleSpawner] Prefab introuvable à Resources/{resourcePath}");
            return;
        }

        GameObject go = Instantiate(prefab, transform.position, transform.rotation);
        NetworkServer.Spawn(go);
    }
}
