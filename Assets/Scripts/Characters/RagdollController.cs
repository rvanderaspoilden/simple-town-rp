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
/// PAS DE CLAMP DE POSITION : on NE force PAS la position des hanches en physique active. Un write
/// direct sur transform.position d'un Rigidbody dynamique connecté à des joints fait exploser le
/// solveur (les joints tirent comme des malades pour rattraper la téléportation → squelette qui vole
/// dans tous les sens). L'invariant « reste sur le NavMesh » est porté par l'isolation véhicule↔os
/// dans VehicleController.Awake (matrice de collision) : avec aucune force externe, la dérive
/// horizontale sous la seule gravité est marginale.
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

    /// <summary>Transform des hanches — utilisé par la caméra TPS du joueur renversé pour suivre le
    /// corps pendant la chute (cf. PlayerController.OnKnockdownChanged).</summary>
    public Transform Hips => _hips;

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

        // Isolation PAR COLLIDER : on positionne explicitement Collider.excludeLayers sur chaque os
        // pour exclure les layers Interactable/Player/NPC/Item de la détection de contact. C'est une
        // garantie locale (par-instance) qui court-circuite tout ce qui pourrait passer outre la
        // matrice globale (IgnoreLayerCollision sur le véhicule) — WheelCollider, CCD à haute vitesse,
        // contacts spéculatifs Unity. Les os ne réagissent ainsi QU'aux layers physiques inertes
        // (sol/murs/décor/props sur Default/Ground/Wall/Door/Props) et à eux-mêmes (Ragdoll, bone-bone).
        LayerMask exclude = 0;
        foreach (string n in new[] { "Interactable", "Player", "NPC", "Item" }) {
            int l = LayerMask.NameToLayer(n);
            if (l >= 0) exclude |= (1 << l);
        }
        foreach (Rigidbody rb in _boneBodies) {
            if (rb == null) continue;
            foreach (Collider col in rb.GetComponents<Collider>()) {
                if (col != null) col.excludeLayers = exclude;
            }
        }

        // Stabilisation des CharacterJoint posés par RagdollBuilder :
        //   - enableProjection=true (défaut) téléporte chaque frame les os pour respecter la
        //     contrainte ; quand on entre en ragdoll depuis une pose extrême (NPC frappé en pleine
        //     marche), les limites sont déjà violées → corrections violentes → squelette qui vole.
        //   - twistLimitSpring / swingLimitSpring vides = limites DURES → les forces correctrices
        //     sont brutales. On ajoute des springs souples (faible spring + amortissement) pour
        //     ramener doucement les os dans les limites au lieu de les snapper.
        //   - Limites de swing élargies de 35° à 60° (twist de 20° à 45°) pour absorber les poses
        //     de marche sans déclencher la correction.
        //   - CollisionDetectionMode passé en Discrete : ContinuousDynamic (défaut RagdollBuilder)
        //     est conçu pour des corps rapides et engendre des contacts spéculatifs instables sur
        //     des os connectés par joints. Discrete est plus stable pour un ragdoll au sol.
        SoftJointLimitSpring softSpring = new SoftJointLimitSpring { spring = 50f, damper = 10f };
        foreach (Rigidbody rb in _boneBodies) {
            if (rb == null) continue;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            CharacterJoint joint = rb.GetComponent<CharacterJoint>();
            if (joint == null) continue;
            joint.enableProjection  = false;
            joint.twistLimitSpring  = softSpring;
            joint.swingLimitSpring  = softSpring;
            joint.lowTwistLimit  = new SoftJointLimit { limit = -45f };
            joint.highTwistLimit = new SoftJointLimit { limit =  45f };
            joint.swing1Limit    = new SoftJointLimit { limit =  60f };
            joint.swing2Limit    = new SoftJointLimit { limit =  60f };
        }

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
