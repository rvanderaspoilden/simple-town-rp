using System.Collections.Generic;
using Sim.Building;
using Sim.Enums;
using Sim.Interactables;
using UnityEngine;

namespace Sim.Scriptables {
    /// <summary>
    /// Configuration de conteneur de stockage. Un prop devient un conteneur dès que
    /// <see cref="slotCount"/> &gt; 0 : on lui associe alors une Place côté backend
    /// (placeKey = "container:&lt;propUuid&gt;") où les items glissés sont persistés.
    /// </summary>
    [System.Serializable]
    public class ContainerConfig {
        [Tooltip("Nombre de slots de la grille. 0 = le prop n'est PAS un conteneur (champ ignoré).")]
        [Min(0)]
        [SerializeField] private int slotCount = 0;

        [Tooltip("Types d'items acceptés dans le conteneur. Liste vide = TOUS les types autorisés. " +
                 "Sert au filtrage côté UI (drop rejeté) et au gating côté serveur (drop ignoré).")]
        [SerializeField] private List<ItemType> acceptedTypes = new List<ItemType>();

        [Tooltip("Autorise l'emballage de meubles (props) dans ce conteneur via l'action « Emballer ». " +
                 "Le prop est déplacé (UUID conservé) dans la place du conteneur, position null, is_built remis à false.")]
        [SerializeField] private bool acceptsProps = false;

        public int SlotCount => slotCount;
        public IReadOnlyList<ItemType> AcceptedTypes => acceptedTypes;
        public bool IsContainer => slotCount > 0;
        public bool AcceptsProps => acceptsProps;

        /// <summary>True si le conteneur accepte ce type (liste vide = accepte tout).</summary>
        public bool Accepts(ItemType type)
            => acceptedTypes == null || acceptedTypes.Count == 0 || acceptedTypes.Contains(type);
    }

    [CreateAssetMenu(fileName = "Props", menuName = "Configurations/Props")]
    public class PropsConfig : ScriptableObject {
        [SerializeField]
        private int id;

        [SerializeField]
        private string displayName;

        [Tooltip("Description affichée dans la tooltip au survol d'un slot (optionnel).")]
        [SerializeField] [TextArea]
        private string description;

        [SerializeField]
        private Sprite sprite;

        [SerializeField]
        private PropsType propsType;

        [SerializeField]
        private int price;

        [SerializeField, Tooltip("Can this prop be sold? Drives BOTH its availability in the phone shop AND player-to-player listing. Tick on furniture; leave off for doors, lights, delivery boxes, packages, etc.")]
        private bool sellable;

        [SerializeField]
        private PropBehaviourBase prefab;

        [SerializeField]
        private PropsConfig packageConfig;

        [SerializeField]
        private BuildSurfaceEnum surfaceToPose;

        [SerializeField]
        private bool posableOnProps;

        [SerializeField]
        private bool hasPosableSurface;

        [SerializeField]
        private bool connectedToWall;

        [SerializeField]
        private bool toBuild;

        [SerializeField, Tooltip("Can the owner move this prop once built (generic MOVE action)? On by default; untick for fixtures like the delivery box, doors, lights.")]
        private bool movable = true;

        [SerializeField]
        private float rangeToInteract;

        [SerializeField]
        private bool rightClickOnly;

        [SerializeField]
        private Action[] actions;

        [SerializeField]
        private Action[] unbuiltActions;

        [SerializeField]
        private Texture2D cursor;

        [SerializeField]
        private PropsPreset[] presets;

        [SerializeField]
        private AudioClip buildSound;

        [SerializeField, Tooltip("Durée de construction en secondes (le joueur joue une animation + barre de progression). 0 = construction instantanée.")]
        private float buildDuration = 3f;

        [Header("Storage container")]
        [SerializeField] private ContainerConfig container = new ContainerConfig();

        /// <summary>Configuration de conteneur (slots, types acceptés). slotCount=0 = pas un conteneur.</summary>
        public ContainerConfig Container => container;

        public PropsPreset[] Presets => presets;

        public Texture2D GetCursor() {
            return this.cursor;
        }

        public PropsType GetPropsType() {
            return this.propsType;
        }

        public AudioClip BuildSound => buildSound;

        /// <summary>Durée de construction (secondes). 0 = instantané.</summary>
        public float BuildDuration => buildDuration;

        public bool NeedToBeConnectedToWall() {
            return this.connectedToWall;
        }

        public Action[] GetUnbuiltActions() {
            return this.unbuiltActions;
        }

        public Action[] GetActions() {
            return this.actions;
        }

        public bool IsPosableOnProps() {
            return this.posableOnProps;
        }

        public bool IsSellable() {
            return this.sellable;
        }

        public int Price => price;

        public int GetId() {
            return this.id;
        }

        public Sprite Sprite => sprite;

        public float GetRangeToInteract() {
            return this.rangeToInteract;
        }

        public bool IsRightClickOnly() {
            return this.rightClickOnly;
        }

        public PropBehaviourBase GetPrefab() {
            return this.prefab;
        }

        public string GetDisplayName() {
            return this.displayName;
        }

        public string Description => description;

        public BuildSurfaceEnum GetSurfaceToPose() {
            return this.surfaceToPose;
        }

        public PropsConfig GetPackageConfig() {
            return this.packageConfig;
        }

        public bool HasPosableSurface => hasPosableSurface;

        public bool MustBeBuilt() {
            return this.toBuild;
        }

        public bool IsMovable() {
            return this.movable;
        }
    }
}