using Sim;
using Sim.UI;
using UnityEngine;

/// <summary>
/// Active le <see cref="MinimapMarker"/> porté par ce véhicule UNIQUEMENT pour le joueur LOCAL
/// quand il en est propriétaire ET qu'il n'est pas à bord (conducteur/passager). Sur les autres
/// clients — ou quand le propriétaire monte à bord — le marker est désactivé : le joueur n'a pas
/// besoin de l'icône de sa voiture quand il l'a directement sous le pied.
///
/// Le composant frère <see cref="MinimapMarker"/> est pré-configuré dans le prefab véhicule
/// (config = « Vehicle Marker » asset, roomId = « city ») et laissé désactivé : on l'allume/éteint
/// chaque frame selon les conditions courantes — propriété + bord (parentage sous le véhicule).
/// Pas de SyncVar hook nécessaire : l'overhead d'un check par frame est négligeable et survit
/// proprement aux changements d'autorité, de driver, de passagers et de spawn local.
///
/// Namespace global (cohérent avec <see cref="VehicleController"/> et les types réseau Mirror).
/// </summary>
[RequireComponent(typeof(MinimapMarker))]
[RequireComponent(typeof(VehicleController))]
public class VehicleMinimapMarker : MonoBehaviour {
    private MinimapMarker     _marker;
    private VehicleController _vehicle;

    private void Awake() {
        _marker  = GetComponent<MinimapMarker>();
        _vehicle = GetComponent<VehicleController>();
        if (_marker != null) _marker.enabled = false; // affichage piloté par Update()
    }

    private void Update() {
        if (_marker == null || _vehicle == null) return;
        bool show = ShouldShow();
        if (_marker.enabled != show) _marker.enabled = show;
    }

    /// <summary>Vrai si le joueur local possède ce véhicule ET n'est pas assis dedans
    /// (les sièges driver/passenger sont parentés sous le transform du véhicule).</summary>
    private bool ShouldShow() {
        PlayerController local = PlayerController.Local;
        if (local == null || local.CharacterData == null) return false;
        if (local.CharacterData.Id != _vehicle.OwnerCharacterId) return false;
        if (local.transform.IsChildOf(_vehicle.transform)) return false;
        return true;
    }
}
