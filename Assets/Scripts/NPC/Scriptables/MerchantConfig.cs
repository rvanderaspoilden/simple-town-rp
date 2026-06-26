using System.Collections.Generic;
using UnityEngine;

namespace Sim.NPC {
    /// <summary>
    /// Configuration d'un NPC marchand. Asset autoré par un designer puis assigné au champ
    /// <c>merchantConfig</c> d'un <see cref="NpcSpawnPoint"/> : le spawn point devient alors un
    /// « stand » et le NPC qui en sort tient ce stand (état <see cref="NpcStateType.Merchant"/>,
    /// interactable côté client → action BUY).
    ///
    /// La présence (non-null) de cette config est l'unique commutateur : pas de prefab ni de
    /// controller dédié. Le sous-graphe marchand est câblé dans <c>NpcAIController.BuildStateMachine</c>
    /// quand <c>IsMerchant</c> est vrai.
    ///
    /// VEND DES ITEMS UNIQUEMENT (objets à main, <see cref="ItemConfig"/>) — pas de props/meubles.
    /// </summary>
    [CreateAssetMenu(menuName = "Configurations/Merchant", fileName = "New Merchant")]
    public class MerchantConfig : ScriptableObject {
        [Tooltip("Libellé affiché en en-tête de la boutique (ex : « Étal de Marius »).")]
        [SerializeField] private string merchantLabel = "Marchand";

        [Tooltip("Items vendus, achetables un par un. Réutilise ItemPrice { item, price }.")]
        [SerializeField] private List<ItemPrice> catalog = new List<ItemPrice>();

        [Header("Comportement (pauses)")]
        [Tooltip("Probabilité [0..1] qu'à la fin d'une période de tenue le marchand s'absente " +
                 "brièvement (petite errance) au lieu d'enchaîner une nouvelle période au stand.")]
        [Range(0f, 1f)]
        [SerializeField] private float pauseProbability = 0.35f;

        [Tooltip("Durée min/max (s) pendant laquelle le marchand tient son stand avant de " +
                 "potentiellement s'absenter.")]
        [SerializeField] private float minTendSeconds = 12f;
        [SerializeField] private float maxTendSeconds = 25f;

        [Tooltip("Durée min/max (s) d'une absence (errance courte) avant de revenir au stand.")]
        [SerializeField] private float minPauseSeconds = 3f;
        [SerializeField] private float maxPauseSeconds = 7f;

        [Tooltip("Rayon (m) autour du stand dans lequel le marchand erre pendant une pause.")]
        [SerializeField] private float pauseWanderRadius = 2.5f;

        public string MerchantLabel => merchantLabel;
        public IReadOnlyList<ItemPrice> Catalog => catalog;

        public float PauseProbability => Mathf.Clamp01(pauseProbability);
        public float MinTendSeconds   => Mathf.Min(minTendSeconds, maxTendSeconds);
        public float MaxTendSeconds   => Mathf.Max(minTendSeconds, maxTendSeconds);
        public float MinPauseSeconds  => Mathf.Min(minPauseSeconds, maxPauseSeconds);
        public float MaxPauseSeconds  => Mathf.Max(minPauseSeconds, maxPauseSeconds);
        public float PauseWanderRadius => Mathf.Max(0f, pauseWanderRadius);

        /// <summary>
        /// Résout une entrée du catalogue par id de config d'item. Sert au serveur pour valider
        /// qu'un item demandé est bien au catalogue et en récupérer le prix + la config.
        /// </summary>
        public bool TryGetPrice(int itemConfigId, out int price, out ItemConfig cfg) {
            foreach (ItemPrice entry in catalog) {
                if (entry.item != null && entry.item.ID == itemConfigId) {
                    price = entry.price;
                    cfg   = entry.item;
                    return true;
                }
            }
            price = 0;
            cfg   = null;
            return false;
        }
    }
}
