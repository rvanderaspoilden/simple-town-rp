using System.Collections.Generic;
using System.Linq;
using Interaction;
using Mirror;
using Sim;
using Sim.Enums;
using Sim.Entities;
using UnityEngine;
using Action = Sim.Interactables.Action;

/// <summary>
/// Porte de garage interactive — OBJET DE SCÈNE NON-RÉSEAU (pas de NetworkIdentity). Présente sur
/// tous les clients (objet de scène) ET sur le serveur. Deux actions au menu radial :
///   • « Sortir un véhicule » → récupère les véhicules possédés (REST) et ouvre un sous-menu radial ;
///     choisir un véhicule envoie <see cref="C2S_TakeOutVehicle"/> → le serveur le SPAWN à
///     <see cref="spawnPoint"/> (conduisible).
///   • « Ranger le véhicule » (si un véhicule possédé est à portée) → envoie <see cref="C2S_StoreVehicle"/>
///     → le serveur détruit le véhicule du joueur le plus proche dans <see cref="storeRadius"/>.
///
/// La logique serveur est dans <see cref="VehicleSystemBootstrap"/>, qui retrouve la porte via le
/// registre statique (clé <see cref="doorKey"/>). « Sorti » est un état runtime (un VehicleController
/// spawné portant l'id DB du véhicule) — la position n'est pas persistée.
/// </summary>
public class GarageDoor : MonoBehaviour, IInteractable {
    [Tooltip("Clé unique de cette porte (résolution serveur depuis les messages). Unique par porte.")]
    [SerializeField] private string doorKey = "garage-1";
    [Tooltip("Emplacement (et orientation) d'apparition du véhicule sorti.")]
    [SerializeField] private Transform spawnPoint;
    [Tooltip("Rayon autour de la porte dans lequel un véhicule possédé peut être rangé.")]
    [SerializeField] private float storeRadius = 6f;
    [SerializeField] private float interactionRange = 3f;

    private static readonly Dictionary<string, GarageDoor> _registry = new Dictionary<string, GarageDoor>();

    private Action _takeOutAction;
    private Action _storeAction;
    private Sprite _icon;

    private void Awake() {
        _icon = Resources.Load<Action>("Configurations/Actions/ENTER_VEHICLE")?.Icon;
        _takeOutAction = Action.CreateRuntime(ActionTypeEnum.USE, "Sortir un véhicule", _icon);
        _takeOutAction.OnExecute += OnTakeOutExecuted;
        _storeAction = Action.CreateRuntime(ActionTypeEnum.USE, "Ranger le véhicule", _icon);
        _storeAction.OnExecute += OnStoreExecuted;
    }

    private void OnEnable()  { if (!string.IsNullOrEmpty(doorKey)) _registry[doorKey] = this; }
    private void OnDisable() { if (_registry.TryGetValue(doorKey, out GarageDoor d) && d == this) _registry.Remove(doorKey); }

    private void OnDestroy() {
        if (_takeOutAction != null) _takeOutAction.OnExecute -= OnTakeOutExecuted;
        if (_storeAction != null) _storeAction.OnExecute -= OnStoreExecuted;
    }

    public static bool TryGet(string key, out GarageDoor door) => _registry.TryGetValue(key ?? "", out door);

    // ── IInteractable ───────────────────────────────────────────────────────────────
    public float GetRange()         => interactionRange;
    public bool  IsInteractable()   => true;
    public bool  IsRightClickOnly() => false;
    public void  StopInteraction()  { }

    public Action[] GetActions(bool withPriority = false) {
        var list = new List<Action> { _takeOutAction };
        if (LocalHasStorableVehicleNearby()) list.Add(_storeAction);
        return list.ToArray();
    }

    // ── Sortir un véhicule (client) ─────────────────────────────────────────────────
    private void OnTakeOutExecuted(Action _) {
        PlayerController local = PlayerController.Local;
        Debug.Log($"[GarageDoor] action 'Sortir' exécutée (local={(local!=null)}, charId={local?.CharacterData?.Id}, api={(ApiManager.Instance!=null)})");
        if (local?.CharacterData == null || ApiManager.Instance == null) return;
        ApiManager.Instance.StartCoroutine(
            ApiManager.Instance.RetrieveOwnedVehiclesCoroutine(local.CharacterData.Id, ShowVehicleMenu));
    }

    private void ShowVehicleMenu(List<VehicleData> vehicles) {
        var actions = new List<Action>();
        int garaged = 0;
        foreach (var dv in vehicles) if (dv != null && !string.IsNullOrEmpty(dv.placeId)) garaged++;
        Debug.Log($"[GarageDoor] véhicules reçus={vehicles.Count}, garés (placeId set)={garaged}");
        foreach (VehicleData v in vehicles) {
            if (v == null || string.IsNullOrEmpty(v.placeId)) continue; // véhicules garés uniquement
            if (IsVehicleOut(v.id)) continue;                            // déjà dehors
            string id = v.id;
            VehicleConfig cfg = DatabaseManager.GetVehicleConfigById(v.modelId);
            string label = cfg != null ? cfg.modelName : (string.IsNullOrEmpty(v.modelId) ? "Véhicule" : v.modelId);
            Action act = Action.CreateRuntime(ActionTypeEnum.USE, label, _icon);
            act.OnExecute += __ => NetworkClient.Send(new C2S_TakeOutVehicle { vehicleId = id, doorKey = doorKey });
            actions.Add(act);
        }
        if (actions.Count == 0) {
            WorldToastManager.ShowError("Aucun véhicule à sortir");
            return;
        }
        HUDManager.Instance.ShowContextMenu(actions.ToArray(), transform);
    }

    private void OnStoreExecuted(Action _) => NetworkClient.Send(new C2S_StoreVehicle { doorKey = doorKey });

    private bool LocalHasStorableVehicleNearby() {
        PlayerController local = PlayerController.Local;
        if (local?.CharacterData == null) return false;
        string charId = local.CharacterData.Id;
        float r2 = storeRadius * storeRadius;
        foreach (NetworkIdentity ni in NetworkClient.spawned.Values) {
            VehicleController vc = ni.GetComponent<VehicleController>();
            if (vc == null || string.IsNullOrEmpty(vc.VehicleDbId)) continue;
            if (vc.OwnerCharacterId != charId) continue;
            if ((vc.transform.position - transform.position).sqrMagnitude <= r2) return true;
        }
        return false;
    }

    // ── Serveur (appelé par VehicleSystemBootstrap) ─────────────────────────────────
    public void ServerSpawnVehicle(VehicleData v) {
        VehicleConfig cfg = DatabaseManager.GetVehicleConfigById(v.modelId);
        GameObject prefab = cfg != null ? cfg.prefab : null;
        if (prefab == null) { Debug.LogError($"[GarageDoor] Prefab introuvable pour la config '{v.modelId}'"); return; }
        Vector3 pos = spawnPoint != null ? spawnPoint.position : transform.position;
        Quaternion rot = spawnPoint != null ? spawnPoint.rotation : transform.rotation;
        GameObject go = Instantiate(prefab, pos, rot);
        go.GetComponent<VehicleController>()?.ServerInitFromGarage(v.id, v.ownerCharacterId);
        NetworkServer.Spawn(go);
    }

    public void ServerStoreNearest(string charId) {
        if (string.IsNullOrEmpty(charId)) return;
        float r2 = storeRadius * storeRadius;
        foreach (NetworkIdentity ni in NetworkServer.spawned.Values.ToList()) {
            VehicleController vc = ni.GetComponent<VehicleController>();
            if (vc == null || string.IsNullOrEmpty(vc.VehicleDbId)) continue;
            if (vc.OwnerCharacterId != charId) continue;
            if (vc.IsOccupied) continue;
            if ((vc.transform.position - transform.position).sqrMagnitude > r2) continue;
            NetworkServer.Destroy(vc.gameObject);
            return;
        }
    }

    /// <summary>Vrai si un véhicule portant cet id DB est déjà spawné (côté client ou serveur).</summary>
    public static bool IsVehicleOut(string dbId) {
        var dict = NetworkServer.active ? NetworkServer.spawned : NetworkClient.spawned;
        foreach (NetworkIdentity ni in dict.Values) {
            VehicleController vc = ni.GetComponent<VehicleController>();
            if (vc != null && vc.VehicleDbId == dbId) return true;
        }
        return false;
    }
}
