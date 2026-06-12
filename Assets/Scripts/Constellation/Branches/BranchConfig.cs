using UnityEngine;

namespace Sim.Constellation.Branches {
    /// <summary>
    /// Source unique de vérité pour une devise constellation : branche racine
    /// (Créatif / Sportif / Sociable / Ingénieux) OU sous-branche métier (Livreur, ...).
    ///
    /// Un asset par branche sous <c>Resources/Configurations/Constellation/Branches/</c>. Référencé
    /// par <c>BranchPointRewardKind</c> (côté mission), les nœuds (branch home + cost),
    /// et résolu via <see cref="BranchDatabase.ById"/>. Libellés FR, couleur et flag
    /// d'affichage vivent ici. La clé de devise canonique (runtime + wire JSONB
    /// <c>constellation_states.points</c>) est <see cref="id"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Sim/Constellation/Branch", fileName = "Branch")]
    public class BranchConfig : ScriptableObject {
        [Tooltip("Identifiant stable = clé de devise canonique (clé du JSONB unique " +
                 "constellation_states.points). Bases : \"Creatif\"/\"Ingenieux\"/\"Sportif\"/" +
                 "\"Sociable\" ; sous-branches métier : l'id de devise existant (\"delivery_driver\"). " +
                 "Unique sur TOUT le keyspace (racines ET sous-branches confondues). Ne pas renommer en prod.")]
        public string id;

        [Tooltip("Branche parente (hiérarchie VISUELLE uniquement : layout + regroupement profil). " +
                 "null = branche racine. N'influence PAS le stockage des points — la devise est " +
                 "toujours keyée par `id` dans la map unique.")]
        public BranchConfig parent;

        [Tooltip("Libellé utilisateur en français. Ex: \"Créatif\".")]
        public string displayName;

        [TextArea]
        public string description;

        [Tooltip("Couleur de la branche dans les compteurs, les nœuds et les liens.")]
        public Color color = Color.white;

        [Tooltip("Si false, la branche n'apparaît pas comme compteur dans BROZ PROFILE. " +
                 "Utilisé aujourd'hui pour Ingénieux dont l'arbre Métier est gratuit (pas " +
                 "de devise dépensable côté joueur).")]
        public bool showInProfile = true;
    }
}
