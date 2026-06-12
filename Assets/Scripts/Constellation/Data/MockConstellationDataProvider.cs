using System;
using UnityEngine;

namespace Sim.Constellation {
    // Fournisseur de données mock pour la Phase 1 (prototype UI). État en mémoire, seedé
    // avec un pot de points dépensables et les 4 racines de branche déjà débloquées, pour
    // que le joueur puisse immédiatement dépenser dans les nœuds de son choix.
    public class MockConstellationDataProvider : IConstellationDataProvider {
        public ConstellationGraphConfig Graph { get; }
        public ConstellationState State { get; }

        public event Action<ConstellationNodeData> OnNodeUnlocked;
        public event Action OnStateChanged;

        public MockConstellationDataProvider() {
            // Tente de charger un asset auteuré ; sinon graphe par défaut codé en dur.
            Graph = Resources.Load<ConstellationGraphConfig>("Configurations/Constellation/ConstellationGraph");
            if (Graph == null) Graph = ConstellationGraphConfig.CreateDefault();

            State = new ConstellationState(Graph);

            // Pot de points dépensables initial (profil « Créatif/Sociable » entamé).
            // Keyspace unique keyé par BranchConfig.id. La devise Ingénieux n'est pas
            // seedée — l'arbre Métier est gratuit ; ses sous-nœuds coûtent leur sous-branche.
            State.AddAvailable("Creatif", 32);
            State.AddAvailable("Sociable", 28);
            State.AddAvailable("Sportif", 18);
            State.AddAvailable("delivery_driver", 30); // de quoi tester l'arbre Livreur

            // Nœuds débloqués d'office :
            //  - les 3 racines de branche dépensables (Créatif/Sportif/Sociable)
            //  - la racine de l'arbre Métier (ex Ingénieux)
            //  - chaque racine de métier (Réparation / Construction / Logistique / Livreur)
            // → la modale s'ouvre sur un arbre des métiers déjà visible ; la progression
            //   se fait à l'intérieur de chaque sous-métier via sa devise Profession.
            foreach (var id in DefaultUnlockedNodeIds) State.ForceUnlock(id);
        }

        // Nœuds que tout joueur a débloqués d'office. Centralisé pour pouvoir le réutiliser
        // depuis le BackendConstellationDataProvider (sécurité de défense ; le serveur les
        // pré-remplit déjà dans constellation_states).
        public static readonly string[] DefaultUnlockedNodeIds = new[] {
            "creatif_base",
            "sportif_base",
            "sociable_base",
            "ingenieux_base",
            "ingenious_base",
            "environnement_base",
            "ingenious_delivery_driver",
        };

        public void AddPoints(string branchId, int amount) {
            if (string.IsNullOrEmpty(branchId)) return;
            State.AddAvailable(branchId, amount);
            OnStateChanged?.Invoke();
        }

        public bool TryUnlock(ConstellationNodeData node) {
            if (node == null) return false;
            if (!State.TryUnlock(node)) return false;
            // Important : OnStateChanged d'abord pour que toutes les vues passent au nouvel
            // état (le NodeView recalcule notamment son _baseScale = unlockedScale).
            // Ensuite OnNodeUnlocked déclenche le pop d'anim qui s'appuie sur ce baseScale.
            OnStateChanged?.Invoke();
            OnNodeUnlocked?.Invoke(node);
            return true;
        }
    }
}
