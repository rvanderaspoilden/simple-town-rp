using System;
using AdvancedPeopleSystem;
using UnityEngine;

namespace Sim {
    /// <summary>
    /// Apparence joueur = Advanced People System 2. Fait le pont entre le <see cref="Style"/>
    /// persisté (gender + JSON CharacterCustomizationSetup) et le composant
    /// <see cref="CharacterCustomization"/> du pack, porté par l'enfant rig (« Render »).
    ///
    /// L'Animator de GAMEPLAY reste sur la racine (OnAnimatorIK de PlayerShooter, NetworkAnimator,
    /// GetComponent&lt;Animator&gt; partout) : après chaque (re)build du rig APS2, l'avatar du settings
    /// est migré sur l'Animator racine, l'Animator que le pack ajoute sur l'enfant est détruit, et
    /// le controller gameplay (Character.controller) est réappliqué.
    ///
    /// Un switch de settings (Male ↔ Female) reconstruit tout le squelette → les ancres de mains/dos,
    /// la cible caméra et le rig ragdoll sont recréés puis recâblés (PlayerHands, PlayerController,
    /// RagdollController). Sur un prefab de preview (menu), ces composants sont absents : null-safe.
    /// </summary>
    public class CharacterAppearance : MonoBehaviour {
        [Header("Advanced People System 2")]
        [SerializeField, Tooltip("Composant du pack, sur l'enfant rig (« Render » pour le joueur).")]
        private CharacterCustomization customization;

        [SerializeField]
        private CharacterSettings maleSettings;

        [SerializeField]
        private CharacterSettings femaleSettings;

        [SerializeField, Tooltip("Controller gameplay réappliqué après chaque rebuild (celui des settings APS2 est ignoré).")]
        private RuntimeAnimatorController animatorController;

        [Header("Ancres (recréées après chaque rebuild de rig)")]
        [SerializeField]
        private Vector3 handAnchorLocalPosition = new Vector3(0f, 0.08f, 0.03f);

        [SerializeField]
        private Vector3 handAnchorLocalEuler = new Vector3(270f, 180f, 0f);

        [SerializeField]
        private Vector3 backAnchorLocalPosition = Vector3.zero;

        [SerializeField]
        private Vector3 backAnchorLocalEuler = Vector3.zero;

        [SerializeField, Tooltip("Hauteur locale du TargetForCamera (enfant du conteneur rig).")]
        private float cameraTargetHeight = 1.45f;

        [Header("Peau (slider 0-1 du menu)")]
        [SerializeField]
        private Color skinColorLight = new Color(0.94f, 0.78f, 0.66f);

        [SerializeField]
        private Color skinColorDark = new Color(0.45f, 0.26f, 0.16f);

        private Animator rootAnimator;
        private PlayerHands playerHands;
        private PlayerController playerController;
        private RagdollController ragdollController;

        private Gender gender = Gender.MALE;
        private float skinColorPercent;
        private string lastAppliedAdvanced;

        // Garde-robe pilotée par les ITEMS équipés (place wearables, SyncVar equippedClothing).
        // null = jamais reçue → le style APS2 s'applique intégralement (NPC, preview menu, et
        // joueurs legacy le temps de l'hydratation/migration serveur). Une fois reçue, les 5
        // catégories vestimentaires du style sont REMPLACÉES par ces indices à chaque apply.
        private string wornClothingCsv;
        private int[] wornElementIndices;

        // Sac à dos piloté par l'ITEM porté sur le dos (PlayerHands.BackItem → BackpackConfig).
        // Sentinelle « jamais reçu » : le style APS2 garde alors la main sur Item1 (NPC, preview).
        private const int WornBackpackUnset = -2;
        private int wornBackpackIndex = WornBackpackUnset;

        /// <summary>Déclenché après un rebuild complet du rig (os recréés).</summary>
        public event Action OnRigRebuilt;

        public Gender Gender => gender;

        public float SkinColorPercent => skinColorPercent;

        public CharacterCustomization Customization => customization;

        private void Awake() {
            this.rootAnimator = GetComponent<Animator>();
            this.playerHands = GetComponent<PlayerHands>();
            this.playerController = GetComponent<PlayerController>();
            this.ragdollController = GetComponent<RagdollController>();

            if (this.customization != null && this.customization.selectedsettings == this.femaleSettings) {
                this.gender = Gender.FEMALE;
            }
        }

        #region Style (persistance backend)

        /// <summary>Applique le style persisté : assure le bon rig (gender) puis rejoue le setup
        /// APS2 complet. Idempotent : ne refait rien si le même style est déjà appliqué (le hook
        /// SyncVar rejoue le CharacterData à chaque update serveur — argent, mood, etc.).</summary>
        public void ApplyStyle(Style style) {
            bool rigChanged = EnsureRig(style.gender);

            string advanced = style.advanced ?? string.Empty;
            if (!rigChanged && advanced == this.lastAppliedAdvanced) return;
            this.lastAppliedAdvanced = advanced;

            CharacterCustomizationSetup setup = null;
            if (!string.IsNullOrEmpty(advanced)) {
                try {
                    setup = CharacterCustomizationSetup.Deserialize(advanced, CharacterCustomizationSetup.CharacterFileSaveFormat.Json);
                } catch (Exception e) {
                    Debug.LogWarning($"[CharacterAppearance] Setup APS2 illisible, fallback défauts: {e.Message}");
                }
            }

            if (setup != null) {
                ApplySetup(setup);
            } else {
                // Fallback déterministe (anciens personnages sans setup APS2) : les éléments par
                // défaut des settings — identiques sur tous les clients, contrairement à un random.
                this.customization.ResetAll(false);
            }

            // Les vêtements item-driven priment sur ceux du style (ordre d'arrivée des SyncVars
            // rawCharacterData / equippedClothing indifférent : chacune ré-applique l'autre volet).
            ApplyWornClothing();
            ApplyWornBackpack();
        }

        /// <summary>Garde-robe depuis les ITEMS équipés (hook SyncVar equippedClothing) : csv de
        /// 5 ItemConfig ids dans l'ordre des slots wearables, champ vide = catégorie nue. Chaîne
        /// vide/null = « pas encore calculé » → ignoré (le style reste appliqué tel quel).</summary>
        public void SetWornClothing(string csv) {
            if (string.IsNullOrEmpty(csv) || csv == this.wornClothingCsv) return;
            this.wornClothingCsv = csv;

            string[] parts = csv.Split(',');
            var indices = new int[ClothingConfig.WearSlotCount];
            for (int slot = 0; slot < indices.Length; slot++) {
                indices[slot] = -1;
                if (slot >= parts.Length || !int.TryParse(parts[slot], out int configId)) continue;
                if (DatabaseManager.GetItemConfigById(configId) is ClothingConfig clothing) {
                    indices[slot] = clothing.ElementIndex;
                }
            }

            this.wornElementIndices = indices;
            ApplyWornClothing();
        }

        /// <summary>Oublie la garde-robe item (preview du menu : re-bind d'un AUTRE personnage —
        /// sans ce reset, le guard d'idempotence garderait les vêtements du précédent).</summary>
        public void ClearWornClothing() {
            this.wornClothingCsv = null;
            this.wornElementIndices = null;
            this.wornBackpackIndex = WornBackpackUnset;
        }

        /// <summary>Sac à dos porté depuis l'ITEM du slot dos (PlayerHands.NotifyChanged) : index
        /// de preset APS2 Item1, -1 = dos nu. Prime sur l'Item1 du style une fois reçu — comme la
        /// garde-robe, ré-appliqué en fin d'ApplyStyle (rebuild de rig, restyle serveur).</summary>
        public void SetWornBackpack(int apsElementIndex) {
            int value = Mathf.Max(apsElementIndex, -1);
            if (value == this.wornBackpackIndex) return;
            this.wornBackpackIndex = value;
            ApplyWornBackpack();
        }

        private void ApplyWornBackpack() {
            if (this.wornBackpackIndex == WornBackpackUnset) return;

            CharacterCustomization cc = this.customization;
            if (cc == null || cc.Settings == null) return;

            int index = this.wornBackpackIndex;
            var presets = cc.Settings.GetCharacterElementsPresets(CharacterElementType.Item1);
            if (index >= (presets?.Count ?? 0)) index = -1;

            cc.SetElementByIndex(CharacterElementType.Item1, index);
        }

        private void ApplyWornClothing() {
            if (this.wornElementIndices == null) return;

            CharacterCustomization cc = this.customization;
            if (cc == null || cc.Settings == null) return;

            for (int slot = 0; slot < this.wornElementIndices.Length; slot++) {
                CharacterElementType type = ClothingConfig.ElementTypeForSlot(slot);
                int index = this.wornElementIndices[slot];

                // Trou de garde-robe (ex. « Pantalon 11 » absent côté female) : nu plutôt
                // qu'un SetElementByIndex hors bornes.
                var presets = cc.Settings.GetCharacterElementsPresets(type);
                if (index >= (presets?.Count ?? 0)) index = -1;

                cc.SetElementByIndex(type, index);
            }
        }

        /// <summary>Ré-applique INTÉGRALEMENT le vrai look (style + vêtements/sac item-driven)
        /// en forçant le guard d'idempotence. Sert à la cabine d'essayage : la preview pose des
        /// éléments APS2 directement (SetElement) sans toucher aux états suivis — ce reset
        /// restaure l'état réel à chaque changement de panier et à la sortie.</summary>
        public void ReapplyLook(Style style) {
            this.lastAppliedAdvanced = null;
            ApplyStyle(style);
        }

        /// <summary>Snapshot du look courant au format persisté.</summary>
        public Style GetStyle() {
            return new Style {
                gender = this.gender,
                advanced = JsonUtility.ToJson(this.customization.GetSetup())
            };
        }

        /// <summary>Rejoue un setup APS2 sur le rig courant. Équivalent de
        /// CharacterCustomizationSetup.ApplyToCharacter sans l'ApplyPrefab (éditeur).</summary>
        private void ApplySetup(CharacterCustomizationSetup setup) {
            CharacterCustomization cc = this.customization;

            SetColorIfPresent(BodyColorPart.Skin, setup.SkinColor);
            SetColorIfPresent(BodyColorPart.Eye, setup.EyeColor);
            SetColorIfPresent(BodyColorPart.Hair, setup.HairColor);
            SetColorIfPresent(BodyColorPart.Underpants, setup.UnderpantsColor);
            SetColorIfPresent(BodyColorPart.Teeth, setup.TeethColor);
            SetColorIfPresent(BodyColorPart.OralCavity, setup.OralCavityColor);

            cc.SetElementByIndex(CharacterElementType.Head, setup.selectedElements.Head);
            cc.SetElementByIndex(CharacterElementType.Hair, setup.selectedElements.Hair);
            cc.SetElementByIndex(CharacterElementType.Accessory, setup.selectedElements.Accessory);
            cc.SetElementByIndex(CharacterElementType.Hat, setup.selectedElements.Hat);
            cc.SetElementByIndex(CharacterElementType.Pants, setup.selectedElements.Pants);
            cc.SetElementByIndex(CharacterElementType.Shoes, setup.selectedElements.Shoes);
            cc.SetElementByIndex(CharacterElementType.Shirt, setup.selectedElements.Shirt);
            cc.SetElementByIndex(CharacterElementType.Beard, setup.selectedElements.Beard);
            cc.SetElementByIndex(CharacterElementType.Item1, setup.selectedElements.Item1);

            cc.SetHeight(setup.Height);
            cc.SetHeadSize(setup.HeadSize);

            if (setup.blendshapes != null) {
                foreach (CharacterBlendshapeData blendshape in setup.blendshapes) {
                    cc.SetBlendshapeValue(blendshape.type, blendshape.value);
                }
            }
        }

        private void SetColorIfPresent(BodyColorPart part, float[] rgba) {
            if (rgba == null || rgba.Length < 4) return;
            this.customization.SetBodyColor(part, new Color(rgba[0], rgba[1], rgba[2], rgba[3]));
        }

        #endregion

        #region Customisation (UI création / V2 launcher)

        /// <summary>Look aléatoire via le générateur du pack (usage local UI — ne pas appeler côté
        /// réplication : chaque client tirerait un look différent).</summary>
        public void Randomize() {
            EnsureRig(UnityEngine.Random.value >= 0.5f ? Gender.FEMALE : Gender.MALE);
            this.customization.Randomize();
            SetSkinColorPercent(UnityEngine.Random.value);
            this.lastAppliedAdvanced = null;
        }

        public void SetGender(Gender newGender) {
            EnsureRig(newGender);
        }

        /// <summary>Fait défiler un élément (coiffure, haut, bas...) de ±1 ; -1 = aucun (retiré),
        /// avec bouclage dans les deux sens.</summary>
        public void CycleElement(CharacterElementType type, int direction) {
            int count = GetElementCount(type);
            if (count == 0) return;

            int current = this.customization.characterSelectedElements.GetSelectedIndex(type);
            int next = current + (direction >= 0 ? 1 : -1);
            if (next >= count) next = -1;
            else if (next < -1) next = count - 1;

            this.customization.SetElementByIndex(type, next);
        }

        /// <summary>Index de preset actuellement sélectionné pour une catégorie (-1 = aucun).</summary>
        public int GetSelectedElement(CharacterElementType type) {
            return this.customization.characterSelectedElements.GetSelectedIndex(type);
        }

        /// <summary>Applique directement un élément (menu de création — choix contraints).</summary>
        public void SetElement(CharacterElementType type, int index) {
            this.customization.SetElementByIndex(type, index);
        }

        public int GetElementCount(CharacterElementType type) {
            var presets = this.customization.Settings != null
                ? this.customization.Settings.GetCharacterElementsPresets(type)
                : null;
            return presets?.Count ?? 0;
        }

        public void SetBodyColor(BodyColorPart part, Color color) {
            this.customization.SetBodyColor(part, color);
        }

        /// <summary>Couleur de peau pilotée par le slider 0-1 historique du menu.</summary>
        public void SetSkinColorPercent(float percent) {
            this.skinColorPercent = Mathf.Clamp01(percent);
            this.customization.SetBodyColor(BodyColorPart.Skin, Color.Lerp(this.skinColorLight, this.skinColorDark, this.skinColorPercent));
        }

        #endregion

        #region Rig pipeline

        /// <summary>Garantit que le rig APS2 correspond au gender voulu. Retourne true si le rig a
        /// été reconstruit (os/ancres/ragdoll recréés).</summary>
        public bool EnsureRig(Gender targetGender) {
            CharacterSettings target = targetGender == Gender.FEMALE ? this.femaleSettings : this.maleSettings;
            this.gender = targetGender;

            bool rigValid = this.customization.selectedsettings == target && this.customization.originHip != null;
            if (rigValid) return false;

            RebuildRig(target);
            return true;
        }

        private void RebuildRig(CharacterSettings settings) {
            CharacterCustomization cc = this.customization;
            GameObject rigGO = cc.gameObject;

            cc.InitializeMeshes(settings, true);

            // L'Animator de gameplay vit sur la racine : on y migre l'avatar du settings et on
            // supprime celui que le pack vient de poser/configurer sur l'enfant rig.
            Animator rigAnimator = rigGO.GetComponent<Animator>();
            Animator target = this.rootAnimator != null ? this.rootAnimator : rigAnimator;

            if (this.rootAnimator != null && rigAnimator != null && rigAnimator != this.rootAnimator) {
                DestroyImmediate(rigAnimator);
                cc.animator = this.rootAnimator;
            }

            target.avatar = settings.Avatar;
            if (this.animatorController != null) {
                target.runtimeAnimatorController = this.animatorController;
            }
            target.Rebind();

            RebuildAnchors(target, rigGO.transform);
            RebuildRagdoll(target);

            this.lastAppliedAdvanced = null;
            OnRigRebuilt?.Invoke();
        }

        private void RebuildAnchors(Animator animator, Transform rigRoot) {
            Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform chest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
            if (chest == null) chest = animator.GetBoneTransform(HumanBodyBones.Chest);

            Transform right = CreateAnchor(rightHand, "Handle Position", this.handAnchorLocalPosition, this.handAnchorLocalEuler);
            Transform left = CreateAnchor(leftHand, "Handle Position", this.handAnchorLocalPosition, this.handAnchorLocalEuler);
            Transform back = CreateAnchor(chest, "Back Anchor", this.backAnchorLocalPosition, this.backAnchorLocalEuler);

            if (this.playerHands != null) {
                this.playerHands.SetAnchors(left, right, back);
            }

            Transform cameraTarget = CreateAnchor(rigRoot, "TargetForCamera", new Vector3(0f, this.cameraTargetHeight, 0f), Vector3.zero);
            if (this.playerController != null) {
                this.playerController.SetHeadTargetForCamera(cameraTarget);
            }
        }

        private static Transform CreateAnchor(Transform parent, string name, Vector3 localPosition, Vector3 localEuler) {
            if (parent == null) return null;

            Transform anchor = parent.Find(name);
            if (anchor == null) {
                anchor = new GameObject(name).transform;
                anchor.SetParent(parent, false);
            }
            anchor.localPosition = localPosition;
            anchor.localEulerAngles = localEuler;
            return anchor;
        }

        /// <summary>Ré-applique les hideParts APS2 des éléments portés : les morceaux de corps
        /// couverts (CHEST, STOMACH, LEGS…) sont cachés par renderer.enabled=false au moment où
        /// un vêtement est mis. Tout code qui ré-allume les renderers en masse (ex. sortie du
        /// masquage de téléportation de PlayerController) rallume AUSSI ces morceaux — la peau
        /// transperce alors les vêtements. Appeler ceci juste après pour re-cacher.</summary>
        public void ReapplyClothesHideParts() {
            CharacterCustomization cc = this.customization;
            if (cc == null || cc.Settings == null) return;

            foreach (CharacterElementType type in (CharacterElementType[])Enum.GetValues(typeof(CharacterElementType))) {
                int index = cc.characterSelectedElements.GetSelectedIndex(type);
                if (index < 0) continue;

                var presets = cc.Settings.GetCharacterElementsPresets(type);
                if (presets == null || index >= presets.Count) continue;

                CharacterElementsPreset preset = presets[index];
                if (preset != null && preset.hideParts != null && preset.hideParts.Length > 0) {
                    cc.HideParts(preset.hideParts);
                }
            }
        }

        private void RebuildRagdoll(Animator animator) {
            if (this.ragdollController == null) return;

            RagdollRigBuilder.Build(animator);
            this.ragdollController.Reinitialize();
        }

        #endregion
    }
}
