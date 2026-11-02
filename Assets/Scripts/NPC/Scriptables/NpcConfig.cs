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

        [Tooltip("Icône optionnelle affichée au-dessus du NPC (billboard monde). Sert à signaler " +
                 "le type de marchand (étal, concessionnaire…). Vide → rien d'affiché.")]
        [SerializeField] private Sprite icon;

        [Tooltip("Points de vie du NPC (dégâts d'armes : balles, mêlée, poings). À 0 il meurt : " +
                 "ragdoll au sol quelques secondes puis despawn — son point de spawn respawne " +
                 "après le cooldown normal.")]
        [SerializeField] private float maxHealth = 100f;

        [Header("Loot (fouille du cadavre)")]
        [Tooltip("Fourchette de cash lâchée à la fouille du cadavre (liasse en poche du " +
                 "fouilleur). 0-0 = pas de cash. Une seule fouille par corps.")]
        [SerializeField] private int lootCashMin = 5;
        [SerializeField] private int lootCashMax = 20;

        [Tooltip("Items lâchés à la fouille : chaque entrée est tirée indépendamment (chance " +
                 "0..1) et donnée au fouilleur via l'inventaire (poche d'abord).")]
        [SerializeField] private NpcLootEntry[] loot;

        public string     Id           => id;
        public GameObject ServerPrefab => serverPrefab;
        public GameObject ClientPrefab => clientPrefab;

        /// <summary>True si ce NPC est un marchand. Surchargé par <see cref="MerchantNpcConfig"/>.</summary>
        public virtual bool IsMerchant => false;

        /// <summary>Dialogue inline, ou null s'il est vide (le client fera alors le fallback défaut).</summary>
        public DialogueConfig Dialogue => (dialogue != null && dialogue.HasContent) ? dialogue : null;

        /// <summary>Icône au-dessus du NPC (type de marchand), ou null si aucune.</summary>
        public Sprite Icon => icon;

        /// <summary>Points de vie au spawn (consommés par NpcAIController.ServerApplyDamage).</summary>
        public float MaxHealth => Mathf.Max(1f, maxHealth);

        /// <summary>Cash minimum/maximum lâché à la fouille (0/0 = aucun).</summary>
        public int LootCashMin => Mathf.Max(0, Mathf.Min(lootCashMin, lootCashMax));
        public int LootCashMax => Mathf.Max(0, Mathf.Max(lootCashMin, lootCashMax));

        /// <summary>Entrées de loot item (jamais null).</summary>
        public NpcLootEntry[] Loot => loot ?? System.Array.Empty<NpcLootEntry>();
    }

    /// <summary>Une entrée de loot de cadavre NPC : un item tiré à la fouille avec sa
    /// probabilité. Donné au fouilleur via SpawnItemIntoInventory (poche d'abord).</summary>
    [System.Serializable]
    public class NpcLootEntry {
        [Tooltip("Config de l'item lâché.")]
        public ItemConfig item;

        [Tooltip("Nombre d'exemplaires donnés quand l'entrée sort.")]
        [Min(1)] public int quantity = 1;

        [Tooltip("Probabilité (0..1) que cette entrée sorte à la fouille.")]
        [Range(0f, 1f)] public float chance = 1f;
    }
}
