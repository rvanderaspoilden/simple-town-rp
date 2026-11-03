using Mirror;
using Sim;
using UnityEngine;

namespace Sim.NPC {
    /// <summary>
    /// Résolution de combat des NPC hostiles (serveur uniquement). Même philosophie que
    /// PlayerShooter / PlayerMelee côté joueur :
    ///   - les PERSONNAGES sont résolus ANALYTIQUEMENT sur leurs positions serveur (les PNJ
    ///     serveur n'ont aucun collider, ceux des joueurs varient selon le mode) ;
    ///   - les raycasts physiques ne servent qu'au DÉCOR (occlusion / ligne de vue), en
    ///     ignorant tout collider appartenant à un personnage.
    /// Les dégâts passent par PlayerHealth.ApplyWeaponDamage (flinch non létal, mort à 0 PV).
    /// Le feedback visuel est diffusé via NpcServerManager.Attack (S2C_NpcAttack).
    /// </summary>
    public static class NpcCombat {
        public  const float ChestHeight    = 1.2f;
        private const float MaxHeightDelta = 2f;

        // Capsule analytique des personnages (mêmes constantes que PlayerShooter).
        private const float CapsuleHeight = 1.8f;
        private const float CapsuleRadius = 0.45f;

        // ── Acquisition / validation de cible ─────────────────────────────────────

        /// <summary>Joueur vivant le plus proche remplissant les critères d'aggro : même room,
        /// dans la ZONE (distance au home ≤ zoneRadius) et à portée de DÉTECTION du NPC.</summary>
        public static PlayerController FindTarget(NpcAIController npc, HostileNpcConfig cfg) {
            PlayerController best = null;
            float bestSqr = cfg.AggroRadius * cfg.AggroRadius;
            float zoneSqr = cfg.ZoneRadius * cfg.ZoneRadius;
            Vector3 npcPos = npc.transform.position;
            Vector3 home   = npc.HomePosition;

            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values) {
                PlayerController pc = conn?.identity != null ? conn.identity.GetComponent<PlayerController>() : null;
                if (pc == null || pc.IsDead) continue;
                if (PlayerRoomTracker.Instance == null
                    || PlayerRoomTracker.Instance.GetRoom(conn) != npc.RoomId) continue;

                Vector3 p = pc.transform.position;
                if (HorizontalSqr(p, home) > zoneSqr) continue;

                float dSqr = HorizontalSqr(p, npcPos);
                if (dSqr <= bestSqr) {
                    bestSqr = dSqr;
                    best = pc;
                }
            }
            return best;
        }

        /// <summary>La cible courante reste-t-elle valide ? Vivante, même room, et encore dans
        /// la zone (marge de sortie 10% ; une cible PROVOQUÉE — elle a frappé le NPC — est
        /// tolérée jusqu'à 150% de la zone pour que le NPC riposte aux tireurs en lisière).</summary>
        public static bool TargetValid(NpcAIController npc, HostileNpcConfig cfg,
                                       PlayerController target, bool provoked) {
            if (target == null || target.IsDead) return false;
            if (target.connectionToClient == null) return false;
            if (PlayerRoomTracker.Instance == null
                || PlayerRoomTracker.Instance.GetRoom(target.connectionToClient) != npc.RoomId) return false;

            float keep = cfg.ZoneRadius * (provoked ? 1.5f : 1.1f);
            return HorizontalSqr(target.transform.position, npc.HomePosition) <= keep * keep;
        }

        // ── Ligne de vue ──────────────────────────────────────────────────────────

        /// <summary>Ligne poitrine→poitrine non obstruée par le DÉCOR (les colliders de
        /// personnages, variables/absents côté serveur, sont ignorés).</summary>
        public static bool HasLineOfSight(Vector3 fromFeet, Vector3 toFeet) {
            Vector3 origin = fromFeet + Vector3.up * ChestHeight;
            Vector3 target = toFeet   + Vector3.up * ChestHeight;
            Vector3 to = target - origin;
            float dist = to.magnitude;
            if (dist < 0.05f) return true;

            RaycastHit[] hits = Physics.RaycastAll(origin, to / dist, dist - 0.02f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit h in hits) {
                if (h.collider.GetComponentInParent<PlayerController>() != null) continue;
                if (h.collider.GetComponentInParent<ClientNpcView>()    != null) continue; // vue PNJ (host)
                if (h.collider.GetComponentInParent<NpcAIController>()  != null) continue;
                return false;
            }
            return true;
        }

        // ── Mêlée ─────────────────────────────────────────────────────────────────

        /// <summary>Résout l'IMPACT d'un coup de mêlée (différé de ~0.3 s après le départ du
        /// swing, comme PlayerMelee) sur l'état serveur : cible dans le cône devant le NPC,
        /// ligne de frappe dégagée, dégâts PV. Diffuse le son d'impact (MeleeImpact) si le
        /// coup porte. Renvoie vrai si la cible a été touchée.</summary>
        public static bool ResolveMeleeImpact(NpcAIController npc, PlayerController target,
                                              float damage, float range, float hitAngle) {
            if (target == null || target.IsDead) return false;

            Vector3 forward = npc.transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;

            Vector3 to = target.transform.position - npc.transform.position;
            float heightDelta = Mathf.Abs(to.y);
            to.y = 0f;
            float dist = to.magnitude;
            // Légère tolérance de portée : la cible a pu reculer pendant le windup.
            if (dist > range * 1.15f || heightDelta > MaxHeightDelta) return false;
            if (dist >= 0.3f && Vector3.Angle(forward, to) > hitAngle) return false;

            if (!HasLineOfSight(npc.transform.position, target.transform.position)) return false;

            PlayerHealth health = target.GetComponent<PlayerHealth>();
            bool killed = health != null && health.ApplyWeaponDamage(damage);

            Vector3 chest = target.transform.position + Vector3.up * ChestHeight;
            NpcServerManager.Instance.Attack(npc.NpcId, NpcAttackKind.MeleeImpact, chest);
            Sim.Logging.GameLogger.Player.Info("NpcMeleeHit {NpcId} {TargetNetId} {Killed}",
                npc.NpcId, target.netId, killed);
            return true;
        }

        // ── Tir ───────────────────────────────────────────────────────────────────

        /// <summary>Tire une balle vers la cible : direction poitrine→poitrine + dispersion
        /// (arme + bonus NPC), occlusion décor, puis capsules analytiques sur TOUS les joueurs
        /// vivants (un autre joueur sur la trajectoire peut encaisser). Diffuse le tir
        /// (RangedFire, tracer/flash/son joués par la vue client sur l'arme en main).</summary>
        public static void FireRanged(NpcAIController npc, PlayerController target,
                                      WeaponConfig cfg, float extraSpreadDeg) {
            if (target == null || cfg == null) return;

            Vector3 origin = npc.transform.position + Vector3.up * ChestHeight;
            Vector3 aim    = target.transform.position + Vector3.up * ChestHeight;
            Vector3 dir    = aim - origin;
            if (dir.sqrMagnitude < 0.01f) return;
            dir = ApplySpread(dir.normalized, cfg.SpreadAngle + extraSpreadDeg);

            float maxRange = cfg.MaxRange;
            Vector3 end = origin + dir * maxRange;

            // 1) Occlusion DÉCOR (les personnages sont ignorés — résolus analytiquement).
            float decorDist = maxRange;
            RaycastHit[] hits = Physics.RaycastAll(origin, dir, maxRange,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit h in hits) {
                if (h.collider.GetComponentInParent<PlayerController>() != null) continue;
                if (h.collider.GetComponentInParent<ClientNpcView>()    != null) continue;
                if (h.collider.GetComponentInParent<NpcAIController>()  != null) continue;
                decorDist = h.distance;
                end = h.point;
                break;
            }

            // 2) JOUEURS : capsule analytique — le plus proche touché avant le décor encaisse.
            //    Pas de tir fratricide entre NPC (les hostiles d'un camp ne s'entretuent pas).
            PlayerController hitPlayer = null;
            float best = decorDist;
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values) {
                PlayerController pc = conn?.identity != null ? conn.identity.GetComponent<PlayerController>() : null;
                if (pc == null || pc.IsDead) continue;
                if (RayHitsCapsule(origin, dir, best, pc.transform.position, out float d) && d < best) {
                    best = d;
                    hitPlayer = pc;
                }
            }

            if (hitPlayer != null) {
                end = origin + dir * best;
                PlayerHealth health = hitPlayer.GetComponent<PlayerHealth>();
                // Position du NPC tireur transmise : la victime affiche la direction du coup.
                bool killed = health != null && health.ApplyWeaponDamage(cfg.Damage, npc.transform.position);
                Sim.Logging.GameLogger.Player.Info("NpcShot {NpcId} {TargetNetId} {Killed}",
                    npc.NpcId, hitPlayer.netId, killed);
            }

            NpcServerManager.Instance.Attack(npc.NpcId, NpcAttackKind.RangedFire, end);
        }

        // ── Maths (mêmes formules que PlayerShooter) ──────────────────────────────

        private static float HorizontalSqr(Vector3 a, Vector3 b) {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        /// <summary>Déviation aléatoire dans un cône de demi-angle donné (deg) autour de dir.</summary>
        private static Vector3 ApplySpread(Vector3 dir, float angleDeg) {
            if (angleDeg <= 0f) return dir;
            Vector3 deviated = dir + Random.insideUnitSphere * Mathf.Tan(angleDeg * Mathf.Deg2Rad);
            return deviated.sqrMagnitude > 0.0001f ? deviated.normalized : dir;
        }

        /// <summary>Intersection approchée rayon ↔ capsule verticale de personnage (pieds à
        /// basePos). Copie de PlayerShooter.RayHitsCapsule (formule standard, Ericson).</summary>
        private static bool RayHitsCapsule(Vector3 origin, Vector3 dir, float maxDist,
                                           Vector3 basePos, out float dist) {
            Vector3 p0  = basePos + Vector3.up * CapsuleRadius;
            Vector3 seg = Vector3.up * Mathf.Max(0.01f, CapsuleHeight - 2f * CapsuleRadius);

            Vector3 r = origin - p0;
            float b = Vector3.Dot(dir, seg);
            float c = Vector3.Dot(seg, seg);
            float d = Vector3.Dot(dir, r);
            float e = Vector3.Dot(seg, r);
            float denom = c - b * b; // a = dir·dir = 1

            float t = denom > 1e-5f ? (b * e - c * d) / denom : -d;
            t = Mathf.Clamp(t, 0f, maxDist);
            float s = Mathf.Clamp01((b * t + e) / c);
            t = Mathf.Clamp(Vector3.Dot(p0 + seg * s - origin, dir), 0f, maxDist);

            Vector3 onRay  = origin + dir * t;
            Vector3 onAxis = p0 + seg * s;
            dist = t;
            return (onRay - onAxis).sqrMagnitude <= CapsuleRadius * CapsuleRadius;
        }
    }
}
