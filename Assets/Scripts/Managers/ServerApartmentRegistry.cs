using System.Collections.Generic;
using Mirror;
using Sim;

/// <summary>
/// Server-side registry mapping ApartmentKey ("apt:{street}:{door}") → ApartmentController.
/// The room a player is in is the hall room (one per floor) and is shared by every
/// apartment on that floor — so we cannot key by RoomId. Apartment-targeted handlers
/// resolve their target via TryGet(apartmentKey), TryGetByTenant, or TryGetByConn.
/// </summary>
public class ServerApartmentRegistry {
    public static ServerApartmentRegistry Instance { get; } = new ServerApartmentRegistry();

    private readonly Dictionary<string, ApartmentController> _registry = new Dictionary<string, ApartmentController>();

    private ServerApartmentRegistry() { }

    public void Register(string apartmentKey, ApartmentController controller) {
        _registry[apartmentKey] = controller;
    }

    public void Unregister(string apartmentKey) {
        _registry.Remove(apartmentKey);
    }

    public bool TryGet(string apartmentKey, out ApartmentController controller) =>
        _registry.TryGetValue(apartmentKey, out controller);

    public bool TryGetByTenant(string tenantId, out ApartmentController controller) {
        foreach (var kv in _registry) {
            if (kv.Value.TenantId == tenantId) {
                controller = kv.Value;
                return true;
            }
        }
        controller = null;
        return false;
    }

    /// <summary>
    /// Returns the apartment owned by the character connected via this connection.
    /// Used by C2S handlers (build/edit/save/covers) which only authorise the tenant.
    /// </summary>
    public bool TryGetByConn(NetworkConnectionToClient conn, out ApartmentController controller) {
        controller = null;
        Sim.PlayerController pc = conn?.identity?.GetComponent<Sim.PlayerController>();
        string tenantId = pc?.CharacterData?.Id;
        if (string.IsNullOrEmpty(tenantId)) return false;
        return TryGetByTenant(tenantId, out controller);
    }

    /// <summary>Returns the owner of a given prop, or null if none owns it.</summary>
    public ApartmentController FindOwnerOfProp(int propId) {
        foreach (var kv in _registry) {
            if (kv.Value != null && kv.Value.OwnsProp(propId)) return kv.Value;
        }
        return null;
    }

    public void Clear() => _registry.Clear();
}
