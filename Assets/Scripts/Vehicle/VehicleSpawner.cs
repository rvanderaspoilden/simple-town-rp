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
    [Tooltip("Config du véhicule à faire apparaître (contient le prefab).")]
    [SerializeField] private VehicleConfig config;

    [Tooltip("Clé stable de ce véhicule pour la persistance de propriété (DB). Unique par spawner.")]
    [SerializeField] private string vehicleKey = "city-car-1";

    private void Start() {
        if (!NetworkServer.active) return;

        GameObject prefab = config != null ? config.prefab : null;
        if (prefab == null) {
            Debug.LogError("[VehicleSpawner] Config ou prefab manquant.");
            return;
        }

        GameObject go = Instantiate(prefab, transform.position, transform.rotation);
        NetworkServer.Spawn(go);

        // Hydrate la propriété persistée (table vehicles) à partir de la clé stable.
        go.GetComponent<VehicleController>()?.ServerInitOwnership(vehicleKey);
    }
}
