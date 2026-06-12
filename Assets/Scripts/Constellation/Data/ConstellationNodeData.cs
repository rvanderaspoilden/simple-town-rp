using System;
using System.Collections.Generic;
using Sim.Constellation.Branches;
using UnityEngine;

namespace Sim.Constellation {
    // Données d'un nœud (étoile) du graphe. ScriptableObject DÉDIÉ : un asset par
    // nœud sous Resources/Configurations/Constellation/Nodes/, référencé depuis la
    // liste `nodes` du <see cref="ConstellationGraphConfig"/>. L'état runtime
    // (verrouillé/débloqué) n'est PAS stocké ici — il est calculé par
    // ConstellationState à partir des points de devise.
    //
    // Doit rester dans son propre .cs portant le nom de la classe : Unity exige
    // ce 1:1 fichier/classe pour résoudre la référence MonoScript des SO ; sinon
    // les assets sont écrits avec m_Script: {fileID: 0} → fields null au runtime.
    [CreateAssetMenu(fileName = "Node", menuName = "Sim/Constellation/Node")]
    public class ConstellationNodeData : ScriptableObject {
        public string id;
        public string displayName;
        [TextArea] public string description;

        [Tooltip("Branche d'AFFICHAGE (home) : couleur du nœud + sous-arbre + direction. " +
                 "Découplée du coût — un nœud peut coûter d'autres devises que sa branche home.")]
        public BranchConfig branch;

        [Tooltip("Coût de déblocage : liste agnostique de {branche, montant}. Vide = gratuit. " +
                 "Chaque entrée débite la devise (branche) correspondante.")]
        public List<CostEntry> cost = new List<CostEntry>();

        [Tooltip("Si renseigné, ce nœud est la RACINE d'une (sous-)branche : sa devise " +
                 "apparaît comme compteur dans BROZ PROFILE dès qu'il est débloqué.")]
        public BranchConfig definesBranch;

        // Position sur la carte, en unités UI (pixels du conteneur, repère centré).
        public Vector2 mapPosition;

        [Tooltip("Liens du graphe (références SO directes). Source de vérité pour la topologie.")]
        public ConstellationNodeData[] connectedNodes = Array.Empty<ConstellationNodeData>();

        public string[] unlocks = Array.Empty<string>();
        public string[] activities = Array.Empty<string>();

        // Visuel optionnel de la carte. Null en Phase 1 (mock) → la vue affiche un
        // placeholder teinté par la couleur de branche.
        public Sprite icon;

        // Nœud central « Mon Broz » : toujours débloqué, pas de seuil.
        public bool isCenter;

        [Tooltip("Prérequis additionnels (références SO). ET logique avec le parent de " +
                 "l'arbre couvrant.")]
        public ConstellationNodeData[] extraPrerequisites = Array.Empty<ConstellationNodeData>();
    }
}
