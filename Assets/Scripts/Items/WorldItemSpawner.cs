using System.Collections;
using Mirror;
using UnityEngine;

/// <summary>
/// Marqueur de scène transformant un GameObject en point de spawn d'item-monde géré par
/// <see cref="ServerItemManager"/>. Le système d'items est piloté SERVEUR (pas d'items de scène),
/// donc ce composant ne sert que de repère de position : ses rendus/colliders sont masqués sur
/// TOUS les clients, et côté SERVEUR il fait apparaître l'item réel à sa position. Une réserve de
/// carburant optionnelle est appliquée (bidon d'essence).
///
/// Namespace global (cohérent avec le reste du système d'items).
/// </summary>
public class WorldItemSpawner : MonoBehaviour {
    [Tooltip("Id de l'ItemConfig à faire apparaître ici.")]
    [SerializeField] private int itemConfigId = FuelCanister.ConfigId;
    [Tooltip("Room où spawner l'item (extérieur ville = \"city\").")]
    [SerializeField] private string roomId = "city";

    private void Awake() {
        // Repère purement positionnel : invisible et non-cliquable partout (l'item spawné porte
        // ses propres visuels/collider).
        foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = false;
    }

    private void Start() {
        if (NetworkServer.active) StartCoroutine(SpawnWhenReady());
    }

    private IEnumerator SpawnWhenReady() {
        // ServerItemManager est un singleton paresseux, mais on attend une frame pour laisser
        // l'init réseau/room se mettre en place.
        yield return null;
        // Le carburant initial des bidons est appliqué par SpawnItem (plein, depuis la config).
        ServerItemManager.Instance.SpawnItem(roomId, itemConfigId, transform.position, transform.rotation);
    }
}
