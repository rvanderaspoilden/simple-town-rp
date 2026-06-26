using UnityEngine;

namespace Sim.NPC {
    /// <summary>
    /// Config consolidée d'un NPC. Asset autoré par un designer, rangé sous
    /// <c>Resources/Configurations/NPCs/</c> et assigné au champ <c>npcConfig</c> d'un
    /// <see cref="NpcSpawnPoint"/>. Un seul asset porte TOUTE la configuration d'un NPC :
    /// ses prefabs (serveur + client) et son dialogue. La nature MARCHANDE est portée par la
    /// sous-classe <see cref="MerchantNpcConfig"/> (pas de champ optionnel sur la base).
    ///
    /// DEUX PREFABS — le système NPC est dual-prefab : le <see cref="ServerPrefab"/> (IA + NavMeshAgent,
    /// pooled côté serveur) et le <see cref="ClientPrefab"/> (visuel + animator, instancié côté client).
    /// Comme l'asset est rechargé par id des deux côtés (cf. RÉPLICATION), chaque moitié pioche le
    /// prefab qui la concerne — plus besoin d'un PrefabId ni d'une NpcPrefabDatabase séparée.
    ///
    /// RÉPLICATION PAR ID — au spawn, le serveur ne transmet que <see cref="Id"/> (cf.
    /// <c>S2C_SpawnNpc.ConfigId</c>). Le client recharge le même asset depuis Resources via
    /// <c>DatabaseManager.GetNpcConfigById</c> et en dérive le prefab visuel, l'éventuel label
    /// marchand et tout le dialogue — sans replumbing réseau par champ.
    ///
    /// DÉFAUT — l'asset dont l'<see cref="Id"/> vaut <c>"default"</c> sert de fallback pour les NPC
    /// sans config (passants poolés) : ils empruntent ses prefabs et son dialogue par défaut (cf.
    /// <c>DatabaseManager.DefaultNpcConfig</c>).
    /// </summary>
    [CreateAssetMenu(menuName = "Configurations/NPC", fileName = "New NPC")]
    public class NpcConfig : ScriptableObject {
        [Tooltip("Identifiant unique. Transmis au spawn et utilisé pour le lookup client. " +
                 "L'asset d'id « default » sert de fallback aux NPC sans config.")]
        [SerializeField] private string id = "npc";

        [Tooltip("Prefab serveur (IA + NavMeshAgent). Pooled et piloté côté serveur. " +
                 "Vide → fallback sur les prefabs du NpcConfig « default ».")]
        [SerializeField] private GameObject serverPrefab;

        [Tooltip("Prefab client (visuel + animator). Instancié côté client à la réception du spawn. " +
                 "Vide → fallback sur les prefabs du NpcConfig « default ».")]
        [SerializeField] private GameObject clientPrefab;

        [Tooltip("Dialogue inline du NPC. Vide → fallback sur le dialogue du NpcConfig « default ».")]
        [SerializeField] private DialogueConfig dialogue = new DialogueConfig();

        public string     Id           => id;
        public GameObject ServerPrefab => serverPrefab;
        public GameObject ClientPrefab => clientPrefab;

        /// <summary>True si ce NPC est un marchand. Surchargé par <see cref="MerchantNpcConfig"/>.</summary>
        public virtual bool IsMerchant => false;

        /// <summary>Dialogue inline, ou null s'il est vide (le client fera alors le fallback défaut).</summary>
        public DialogueConfig Dialogue => (dialogue != null && dialogue.HasContent) ? dialogue : null;
    }
}
