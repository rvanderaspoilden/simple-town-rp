using UnityEngine;

/// <summary>
/// Type optionnel d'un point d'intérêt — sert à filtrer les choix d'IA si besoin.
/// Ajouter de nouvelles valeurs ici n'impacte pas la sérialisation réseau (les NPC
/// vivent uniquement côté serveur).
/// </summary>
public enum InterestPointType : byte {
    Generic = 0,
    Bench   = 1,
    Shop    = 2,
    Park    = 3,
    Plaza   = 4
}

/// <summary>
/// Composant à placer dans la scène City pour matérialiser un point d'intérêt
/// que les NPC peuvent visiter. S'enregistre/désenregistre automatiquement
/// auprès de <see cref="InterestPointRegistry"/>.
/// </summary>
public class InterestPoint : MonoBehaviour {
    [SerializeField] private InterestPointType type = InterestPointType.Generic;

    [Tooltip("Si true, le point est ignoré par la sélection d'IA (ex. désactivé temporairement).")]
    [SerializeField] private bool disabled;

    public InterestPointType Type     => type;
    public Vector3           Position => transform.position;
    public bool              IsActive => !disabled && isActiveAndEnabled;

    private void OnEnable()  => InterestPointRegistry.Instance.Register(this);
    private void OnDisable() => InterestPointRegistry.Instance.Unregister(this);

    private void OnDrawGizmos() {
        Gizmos.color = disabled ? Color.gray : new Color(0.2f, 0.8f, 1f, 0.6f);
        Gizmos.DrawSphere(transform.position, 0.4f);
    }
}
