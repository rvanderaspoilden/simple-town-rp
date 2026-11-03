using UnityEngine;

namespace Sim.NPC {
    /// <summary>
    /// Config d'un NPC HOSTILE : il défend une ZONE centrée sur son point de spawn (home).
    /// Tant qu'un joueur vivant est dans la zone et à portée de détection, le NPC le poursuit
    /// et l'attaque — avec son arme (à feu ou de mêlée) ou à mains nues. Hors cible il
    /// PATROUILLE autour de son home ; cible perdue (morte, sortie de zone) il RENTRE et se
    /// soigne. Prendre un coup le provoque (même hors rayon de détection) et alerte les
    /// hostiles voisins (défense de camp).
    ///
    /// La state machine serveur dédiée (Patrol → Chase → Attack → Return) est câblée par
    /// NpcAIController.BuildHostileStateMachine. Les PV / le loot de cadavre viennent de la
    /// base NpcConfig — un garde de camp avec du butin est donc déjà couvert.
    /// </summary>
    [CreateAssetMenu(menuName = "Configurations/NPC Hostile", fileName = "New Hostile NPC")]
    public class HostileNpcConfig : NpcConfig {
        [Header("Aggression")]
        [Tooltip("Arme du NPC : WeaponConfig (arme à feu, ex. Glock/AK) ou MeleeWeaponConfig " +
                 "(ex. hache). VIDE = coups de poing. L'arme est purement visuelle côté client " +
                 "(prefab de l'item en main droite) — dégâts/cadence/portée viennent de sa config.")]
        [SerializeField] private ItemConfig weapon;

        [Tooltip("Rayon de DÉTECTION (m) autour du NPC : un joueur vivant plus proche que ça " +
                 "ET dans la zone déclenche l'aggro.")]
        [SerializeField] private float aggroRadius = 10f;

        [Tooltip("Rayon de la ZONE défendue (m), centrée sur le point de spawn. Le NPC garde sa " +
                 "cible tant qu'elle reste dans la zone (marge de sortie ~10%) ; au-delà il rentre.")]
        [SerializeField] private float zoneRadius = 25f;

        [Tooltip("Rayon de PATROUILLE (m) autour du home quand le NPC n'a pas de cible.")]
        [SerializeField] private float patrolRadius = 8f;

        [Tooltip("Vitesse de course en poursuite (m/s). Au-delà de 1.5 le blend tree passe en Run.")]
        [SerializeField] private float chaseSpeed = 4f;

        [Tooltip("Portée d'attaque (m). 0 = dérivée de l'arme : mêlée ≈ 85% de la portée du coup, " +
                 "arme à feu ≈ 60% de la portée max (plafonné 15 m), poings ≈ 1.4 m.")]
        [SerializeField] private float attackRange = 0f;

        [Tooltip("Temps de réaction (s) avant le premier coup une fois à portée.")]
        [SerializeField] private float reactionDelay = 0.5f;

        [Tooltip("Multiplicateur de cadence MÊLÉE/POINGS appliqué à l'intervalle de l'arme " +
                 "(> 1 = le NPC attaque PLUS LENTEMENT qu'un joueur, pour l'équité).")]
        [SerializeField] private float attackCadenceFactor = 1.5f;

        [Tooltip("Multiplicateur de cadence ARME À FEU appliqué au fireInterval de l'arme. " +
                 "Volontairement élevé : un NPC qui tirerait à la cadence du joueur serait " +
                 "injouable (3 = un tir de Glock ≈ toutes les ~1 s).")]
        [SerializeField] private float rangedCadenceFactor = 3f;

        [Tooltip("Dispersion SUPPLÉMENTAIRE (deg) des tirs du NPC, ajoutée à celle de l'arme " +
                 "(le NPC rate d'autant plus que la cible est loin).")]
        [SerializeField] private float rangedSpreadBonus = 6f;

        [Tooltip("Rayon (m) d'alerte des alliés : quand ce NPC acquiert une cible ou prend un " +
                 "coup, les hostiles voisins (même room) aggro la même cible. 0 = désactivé.")]
        [SerializeField] private float allyAlertRadius = 15f;

        // ── Poings (aucune arme assignée) — alignés sur PlayerMelee ────────────────
        public const float FistDamage    = 5f;
        public const float FistRange     = 1.6f;
        public const float FistHitAngle  = 45f;
        public const float FistInterval  = 0.8f;

        public override bool IsHostile => true;

        public ItemConfig        Weapon       => weapon;
        public WeaponConfig      RangedWeapon => weapon as WeaponConfig;
        public MeleeWeaponConfig MeleeWeapon  => weapon as MeleeWeaponConfig;

        /// <summary>True si l'arme est une arme à feu (tir à distance).</summary>
        public bool IsRanged => weapon is WeaponConfig;

        public float AggroRadius     => Mathf.Max(1f, aggroRadius);
        public float ZoneRadius      => Mathf.Max(AggroRadius, zoneRadius);
        public float PatrolRadius    => Mathf.Max(0f, patrolRadius);
        public float ChaseSpeed      => Mathf.Max(0.5f, chaseSpeed);
        public float ReactionDelay   => Mathf.Max(0f, reactionDelay);
        public float AllyAlertRadius => Mathf.Max(0f, allyAlertRadius);
        public float RangedSpreadBonus => Mathf.Max(0f, rangedSpreadBonus);

        /// <summary>Dégâts par coup, dérivés de l'arme (poings sinon).</summary>
        public float AttackDamage {
            get {
                if (RangedWeapon != null) return RangedWeapon.Damage;
                if (MeleeWeapon  != null) return MeleeWeapon.Damage;
                return FistDamage;
            }
        }

        /// <summary>Intervalle entre deux attaques (s), cadence de l'arme × facteur NPC —
        /// facteur dédié (plus pénalisant) pour les armes à feu.</summary>
        public float AttackInterval {
            get {
                if (RangedWeapon != null) {
                    return RangedWeapon.FireInterval * Mathf.Max(1f, rangedCadenceFactor);
                }
                float baseInterval = MeleeWeapon != null ? MeleeWeapon.AttackInterval : FistInterval;
                return baseInterval * Mathf.Max(0.5f, attackCadenceFactor);
            }
        }

        /// <summary>Portée de la frappe de mêlée (résolution du cône à l'impact).</summary>
        public float MeleeRange    => MeleeWeapon != null ? MeleeWeapon.MaxRange : FistRange;
        /// <summary>Demi-angle (deg) du cône de frappe de mêlée.</summary>
        public float MeleeHitAngle => MeleeWeapon != null ? MeleeWeapon.HitAngle : FistHitAngle;

        /// <summary>Distance (m) à laquelle le NPC s'arrête pour attaquer.</summary>
        public float ResolvedAttackRange {
            get {
                if (attackRange > 0f) return attackRange;
                if (RangedWeapon != null) return Mathf.Min(RangedWeapon.MaxRange * 0.6f, 15f);
                if (MeleeWeapon  != null) return MeleeWeapon.MaxRange * 0.85f;
                return FistRange * 0.9f;
            }
        }
    }
}
