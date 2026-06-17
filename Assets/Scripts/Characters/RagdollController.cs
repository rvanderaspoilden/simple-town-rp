using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Active/désactive un ragdoll physique sur un personnage (joueur OU PNJ). S'auto-configure :
/// découvre les Rigidbody d'os (tous ceux sous ce GameObject, SAUF le Rigidbody racine), leurs
/// colliders, l'Animator et l'os des hanches via l'Animator humanoïde. Aucun câblage manuel requis —
/// fonctionne sur n'importe quel prefab portant déjà un rig ragdoll (créé par RagdollBuilder ou le
/// Ragdoll Wizard).
///
/// Modèle réseau : le ragdoll est COSMÉTIQUE. Chaque client simule sa propre physique à partir de la
/// même pose (état « renversé » répliqué) — aucune physique réseau.
///
/// PAS DE PROJECTION : le ragdoll s'effondre SUR PLACE sous la seule gravité (aucune impulsion). Le
/// personnage ne part pas en vol → les hanches restent ~au-dessus de leur position de départ, donc la
/// relève (repositionnement racine sur les hanches, échantillonné NavMesh) ne le téléporte pas au loin.
///
/// Repos (animé) : les Rigidbody d'os sont kinematic → l'Animator pilote le squelette normalement.
/// Renversé : os non-kinematic (physique), Animator désactivé, collider racine désactivé.
/// </summary>
[DisallowMultipleComponent]
public class RagdollController : MonoBehaviour {
    private Animator    _animator;
    private Collider    _rootCollider;
    private Transform   _hips;
    private Rigidbody[] _boneBodies;
    private bool        _active;

    /// <summary>Le ragdoll est-il actuellement actif (physique en cours) ?</summary>
    public bool IsRagdolling => _active;

    /// <summary>Position monde des hanches — sert à repositionner la racine à la relève.</summary>
    public Vector3 HipsPosition => _hips != null ? _hips.position : transform.position;

    private void Awake() {
        _animator = GetComponentInChildren<Animator>();
        // Collider racine = celui porté par CE GameObject (capsule de gameplay sur layer Player/NPC).
        _rootCollider = GetComponent<Collider>();
        if (_animator != null) _hips = _animator.GetBoneTransform(HumanBodyBones.Hips);

        // Os ragdoll = Rigidbody portés par un GameObject sur le layer « Ragdoll » (posé par
        // RagdollBuilder). Exclut proprement le Rigidbody racine (gameplay) ET tout autre RB non-os
        // (ex. Dissonance), qui ne doivent jamais passer en physique.
        int ragdollLayer = LayerMask.NameToLayer("Ragdoll");
        var bodies = new List<Rigidbody>();
        foreach (Rigidbody rb in GetComponentsInChildren<Rigidbody>(true)) {
            if (ragdollLayer >= 0 && rb.gameObject.layer == ragdollLayer) bodies.Add(rb);
        }
        _boneBodies = bodies.ToArray();

        // État de repos : os kinematic (pilotés par l'animation).
        SetBonesKinematic(true);
    }

    /// <summary>Passe en ragdoll : le squelette s'effondre SUR PLACE sous la gravité (aucune
    /// impulsion, aucune projection). Idempotent : si déjà en ragdoll, ne fait rien.</summary>
    public void EnableRagdoll() {
        if (_boneBodies == null || _boneBodies.Length == 0 || _active) return;
        _active = true;
        if (_animator != null) _animator.enabled = false;
        if (_rootCollider != null) _rootCollider.enabled = false;
        SetBonesKinematic(false);
    }

    /// <summary>Repasse en mode animé (os kinematic, collider + Animator réactivés). Le squelette
    /// re-snappe sur la pose animée RELATIVE à la racine : pense à repositionner la racine sur
    /// <see cref="HipsPosition"/> AVANT d'appeler ceci si tu veux que le perso se relève sur place.</summary>
    public void DisableRagdoll() {
        if (!_active) return;
        _active = false;
        SetBonesKinematic(true);
        if (_rootCollider != null) _rootCollider.enabled = true;
        if (_animator != null) _animator.enabled = true;
    }

    private void SetBonesKinematic(bool kinematic) {
        if (_boneBodies == null) return;
        foreach (Rigidbody rb in _boneBodies) {
            if (rb == null) continue;
            rb.isKinematic = kinematic;
            rb.detectCollisions = !kinematic;
            if (!kinematic) {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
    }
}
