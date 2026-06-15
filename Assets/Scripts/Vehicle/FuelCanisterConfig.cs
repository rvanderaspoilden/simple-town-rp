using UnityEngine;

/// <summary>
/// Config spécifique du bidon d'essence : étend <see cref="ItemConfig"/> avec la capacité du
/// réservoir du bidon (litres). On SOUS-CLASSE plutôt que de polluer ItemConfig (même pattern que
/// <c>ConsumableConfig</c>). L'asset du bidon est de ce type ; capacité éditable dans l'inspecteur.
/// </summary>
[CreateAssetMenu(fileName = "New Fuel Canister", menuName = "Configurations/Item/Fuel Canister")]
public class FuelCanisterConfig : ItemConfig {
    [Tooltip("Capacité du bidon (litres) — il spawn plein à cette valeur.")]
    [Min(0f)] public float fuelCapacity = 20f;
}
