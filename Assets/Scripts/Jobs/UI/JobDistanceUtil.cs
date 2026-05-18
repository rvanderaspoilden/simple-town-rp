using UnityEngine;
using UnityEngine.AI;

namespace Sim.Jobs {
    /// <summary>
    /// Calcule la distance navigable (NavMesh) entre deux positions monde.
    /// Tombe en fallback distance euclidienne si pas de path valide.
    /// Format affichable via FormatMeters.
    /// </summary>
    public static class JobDistanceUtil {
        // Buffer réutilisé pour éviter l'alloc par appel.
        private static readonly NavMeshPath _scratchPath = new NavMeshPath();

        /// <summary>
        /// Renvoie la distance NavMesh entre `from` et `to`. Fallback sur
        /// Vector3.Distance si le path n'est pas calculable / invalide.
        /// </summary>
        public static float Compute(Vector3 from, Vector3 to) {
            if (!NavMesh.CalculatePath(from, to, NavMesh.AllAreas, _scratchPath)) {
                return Vector3.Distance(from, to);
            }
            if (_scratchPath.status == NavMeshPathStatus.PathInvalid) {
                return Vector3.Distance(from, to);
            }

            float d = 0f;
            var corners = _scratchPath.corners;
            for (int i = 1; i < corners.Length; i++) {
                d += Vector3.Distance(corners[i - 1], corners[i]);
            }

            // Le path peut être Partial : on retourne ce qu'on a + fallback
            // euclidien sur la portion restante (pour ne pas afficher 0).
            if (_scratchPath.status == NavMeshPathStatus.PathPartial && corners.Length > 0) {
                d += Vector3.Distance(corners[corners.Length - 1], to);
            }
            return d;
        }

        public static string FormatMeters(float meters) {
            if (meters < 1f) return "<1 m";
            return Mathf.RoundToInt(meters) + " m";
        }
    }
}
