using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sim.NPC {
    /// <summary>
    /// Action déclenchée par une réponse du joueur dans un dialogue.
    /// Le graphe de dialogue est piloté par config et déroulé ENTIÈREMENT côté client : seules les
    /// actions à enjeu (OpenShop → argent/items) repassent par un message serveur déjà existant.
    /// </summary>
    public enum DialogueAction {
        /// <summary>Continue la conversation : affiche le nœud <c>nextNodeId</c>.</summary>
        GoToNode = 0,
        /// <summary>Ferme la modale de dialogue.</summary>
        EndDialogue = 1,
        /// <summary>Ferme le dialogue et ouvre la boutique du marchand (RequestMerchantCatalog).
        /// N'a de sens que si le NpcConfig porteur a un <c>merchant</c>.</summary>
        OpenShop = 2
    }

    /// <summary>Une réponse proposée au joueur sur un nœud de dialogue.</summary>
    [Serializable]
    public class DialogueOption {
        [Tooltip("Libellé du bouton de réponse (string UI, français).")]
        [TextArea(1, 2)]
        public string text = "…";

        [Tooltip("Ce qui se passe quand le joueur choisit cette réponse.")]
        public DialogueAction action = DialogueAction.EndDialogue;

        [Tooltip("Pour GoToNode : id du nœud à afficher ensuite. Ignoré pour les autres actions.")]
        public string nextNodeId = string.Empty;
    }

    /// <summary>Un nœud de dialogue : ce que dit le NPC + les réponses possibles.</summary>
    [Serializable]
    public class DialogueNode {
        [Tooltip("Identifiant unique du nœud dans ce dialogue (ex. « root », « articles », « bye »).")]
        public string id = "root";

        [Tooltip("Texte prononcé par le NPC. Le token {name} est remplacé par le nom du NPC.")]
        [TextArea(2, 4)]
        public string npcText = string.Empty;

        [Tooltip("Réponses proposées au joueur. Une liste vide ferme implicitement le dialogue.")]
        public List<DialogueOption> options = new List<DialogueOption>();
    }

    /// <summary>
    /// Graphe de dialogue inline d'un <see cref="NpcConfig"/>. Référence les nœuds par id (éditable
    /// dans l'inspecteur sans éditeur custom). Le nœud d'entrée est <see cref="rootNodeId"/>, sinon
    /// le premier nœud de la liste.
    /// </summary>
    [Serializable]
    public class DialogueConfig {
        [Tooltip("Id du nœud d'entrée. Vide → premier nœud de la liste.")]
        public string rootNodeId = "root";

        public List<DialogueNode> nodes = new List<DialogueNode>();

        public bool HasContent => nodes != null && nodes.Count > 0;

        /// <summary>Nœud d'entrée : <see cref="rootNodeId"/> s'il existe, sinon le premier nœud.</summary>
        public DialogueNode GetRootNode() {
            if (!HasContent) return null;
            DialogueNode root = GetNode(rootNodeId);
            return root ?? nodes[0];
        }

        /// <summary>Résout un nœud par id. Retourne null si introuvable.</summary>
        public DialogueNode GetNode(string nodeId) {
            if (nodes == null || string.IsNullOrEmpty(nodeId)) return null;
            foreach (DialogueNode n in nodes) {
                if (n != null && n.id == nodeId) return n;
            }
            return null;
        }
    }
}
