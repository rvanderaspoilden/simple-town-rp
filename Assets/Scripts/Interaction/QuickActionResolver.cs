using Sim.Enums;
using Action = Sim.Interactables.Action;

namespace Interaction {
    /// <summary>
    /// Résolution partagée de l'action rapide (<see cref="IQuickActionProvider"/>) :
    ///   1. le type configuré (ItemConfig/PropsConfig.QuickAction) s'il est proposé et permis ;
    ///   2. sinon, si la cible ne propose qu'UNE SEULE action (liste withPriority déjà filtrée
    ///      de SELL/MOVE/DESTROY), elle est l'action rapide de facto — inutile de la configurer.
    /// Une action interdite (IsForbidden) n'est jamais exécutée en rapide : repli menu radial,
    /// qui affiche la raison grisée.
    /// </summary>
    public static class QuickActionResolver {
        public static bool TryResolve(Action[] available, bool hasConfigured, ActionTypeEnum configured, out Action action) {
            action = null;
            if (available == null) return false;

            if (hasConfigured) {
                foreach (var a in available)
                    if (a != null && a.Type == configured && !a.IsForbidden) { action = a; return true; }
            }

            // Action unique → action rapide de facto.
            Action single = null;
            int count = 0;
            foreach (var a in available) {
                if (a == null) continue;
                if (++count > 1) return false;
                single = a;
            }
            if (single != null && !single.IsForbidden) { action = single; return true; }
            return false;
        }
    }
}
