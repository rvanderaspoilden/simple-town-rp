using System;
using System.Collections.Generic;
using Sim.Entities;
using Sim.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Sim {
    public class CharacterCreationManager : MonoBehaviour {
        [Header("Character creation settings")]
        [SerializeField]
        private RectTransform characterCreationPanel;

        [SerializeField]
        private TMP_InputField firstNameInputField;

        [SerializeField]
        private TMP_InputField lastNameInputField;

        [SerializeField]
        private TMP_InputField entranceDateField;

        [SerializeField]
        private Button joinButton;

        [SerializeField]
        private Image bufferImg;

        [SerializeField]
        private AudioClip bufferSound;

        [SerializeField]
        private AudioSource audioSource;

        [SerializeField]
        private CharacterAppearance characterAppearance;

        [SerializeField]
        private List<CharacterPartButton> characterPartButtons;
        
        [SerializeField]
        private List<GenderSelector> genderSelectors;

        [SerializeField]
        private Slider skinSlider;

        [SerializeField]
        private ColorPaletteController paletteController;

        [SerializeField]
        private float rotationSpeed = 2f;

        private CharacterPartType characterPartSelected;

        private RotatingState characterRotating = RotatingState.NONE;

        // Garde-robe de départ : les boutons vêtements ne cyclent que dans cette liste
        // (les tenues hors liste — métiers… — se trouvent en jeu sous forme d'items).
        private StarterWardrobeConfig starterWardrobe;

        public static CharacterCreationManager Instance;

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(this);
            } else {
                Instance = this;
                this.entranceDateField.text = CommonUtils.GetDate();
                this.entranceDateField.readOnly = true;
            }
        }

        private void OnEnable() {
            ApiManager.OnCharacterCreated += OnCharacterCreated;
            ApiManager.OnCharacterCreationFailed += OnCharacterCreationFailed;
        }

        private void OnDisable() {
            ApiManager.OnCharacterCreated -= OnCharacterCreated;
            ApiManager.OnCharacterCreationFailed -= OnCharacterCreationFailed;
        }

        private void Update() {
            if (characterRotating == RotatingState.LEFT) {
                this.RotateLeft();
            } else if (characterRotating == RotatingState.RIGHT) {
                this.RotateRight();
            }
        }

        public RotatingState CharacterRotating {
            get => characterRotating;
            set => characterRotating = value;
        }

        public void RotateRight() {
            this.characterAppearance.gameObject.transform.Rotate(Vector3.up * Time.deltaTime * this.rotationSpeed);
        }

        public void RotateLeft() {
            this.characterAppearance.gameObject.transform.Rotate(Vector3.up * Time.deltaTime * -this.rotationSpeed);
        }

        public void CreateCharacter() {
            this.joinButton.gameObject.SetActive(false);

            CharacterCreationRequest request = new CharacterCreationRequest {
                firstname = firstNameInputField.text,
                lastname = lastNameInputField.text,
                style = this.characterAppearance.GetStyle(),
                entranceDate = CommonUtils.GetDate()
            };

            ApiManager.Instance.CreateCharacter(request);
        }

        public void CheckValidity() {
            this.joinButton.interactable = firstNameInputField.text != string.Empty &&
                                           lastNameInputField.text != string.Empty;
        }

        private void OnCharacterCreated(CharacterData characterData) {
            this.bufferImg.gameObject.SetActive(true);
            this.audioSource.PlayOneShot(this.bufferSound);
        }

        private void OnCharacterCreationFailed(string err) {
            this.joinButton.gameObject.SetActive(true);
        }

        public void Show() {
            this.characterCreationPanel.gameObject.SetActive(true);

            this.bufferImg.gameObject.SetActive(false);

            this.firstNameInputField.Select();

            CheckValidity();

            this.characterAppearance.Randomize();
            ClampWardrobeToStarterChoices();

            this.skinSlider.value = this.characterAppearance.SkinColorPercent;

            this.genderSelectors.ForEach(x => x.SetActive(x.Gender == this.characterAppearance.Gender));

            this.SetCurrentCharacterPart(CharacterPartType.HAIR);
        }

        public void Hide() {
            this.characterCreationPanel.gameObject.SetActive(false);
        }

        public void SelectRight(string characterPart) {
            CharacterPartType partType = GetCharacterPartType(characterPart);
            CycleElementConstrained(ToElementType(partType), +1);

            this.SetCurrentCharacterPart(partType);
        }

        public void SelectLeft(string characterPart) {
            CharacterPartType partType = GetCharacterPartType(characterPart);
            CycleElementConstrained(ToElementType(partType), -1);

            this.SetCurrentCharacterPart(partType);
        }

        /// <summary>Cycle un élément en respectant la garde-robe de départ pour les catégories
        /// VESTIMENTAIRES (shirt/pants/shoes : jamais nu, uniquement les choix autorisés). Les
        /// catégories corporelles (cheveux, barbe) gardent le cycle complet du pack.</summary>
        private void CycleElementConstrained(AdvancedPeopleSystem.CharacterElementType type, int direction) {
            List<int> allowed = AllowedStarterIndices(type);
            if (allowed == null) {
                this.characterAppearance.CycleElement(type, direction);
                return;
            }
            if (allowed.Count == 0) return;

            int current = this.characterAppearance.GetSelectedElement(type);
            int pos = allowed.IndexOf(current);
            int next = pos < 0
                ? allowed[0]
                : allowed[(pos + (direction >= 0 ? 1 : -1) + allowed.Count) % allowed.Count];
            this.characterAppearance.SetElement(type, next);
        }

        /// <summary>Choix de départ pour une catégorie vestimentaire, null = catégorie non
        /// contrainte (corps). Sans asset StarterWardrobe, tout reste libre.</summary>
        private List<int> AllowedStarterIndices(AdvancedPeopleSystem.CharacterElementType type) {
            bool clothing = type == AdvancedPeopleSystem.CharacterElementType.Shirt
                         || type == AdvancedPeopleSystem.CharacterElementType.Pants
                         || type == AdvancedPeopleSystem.CharacterElementType.Shoes
                         || type == AdvancedPeopleSystem.CharacterElementType.Hat
                         || type == AdvancedPeopleSystem.CharacterElementType.Accessory;
            if (!clothing) return null;
            if (this.starterWardrobe == null) this.starterWardrobe = StarterWardrobeConfig.Load();
            if (this.starterWardrobe == null) return null;
            return this.starterWardrobe.AllowedElementIndices(type);
        }

        /// <summary>Après le Randomize d'ouverture : ramène chaque catégorie vestimentaire dans
        /// la garde-robe de départ (le générateur du pack peut tirer des tenues de métier), et
        /// retire chapeau/accessoire/sac (non proposés à la création — ils se trouvent en jeu).</summary>
        private void ClampWardrobeToStarterChoices() {
            foreach (var type in new[] {
                         AdvancedPeopleSystem.CharacterElementType.Shirt,
                         AdvancedPeopleSystem.CharacterElementType.Pants,
                         AdvancedPeopleSystem.CharacterElementType.Shoes }) {
                List<int> allowed = AllowedStarterIndices(type);
                if (allowed == null || allowed.Count == 0) continue;
                if (!allowed.Contains(this.characterAppearance.GetSelectedElement(type))) {
                    this.characterAppearance.SetElement(type, allowed[UnityEngine.Random.Range(0, allowed.Count)]);
                }
            }

            this.characterAppearance.SetElement(AdvancedPeopleSystem.CharacterElementType.Hat, -1);
            this.characterAppearance.SetElement(AdvancedPeopleSystem.CharacterElementType.Accessory, -1);
            this.characterAppearance.SetElement(AdvancedPeopleSystem.CharacterElementType.Item1, -1);
        }

        /// <summary>Mappe le nom de partie passé par les boutons UI (strings sérialisées dans la
        /// scène) vers l'enum. Historiquement porté par CharacterStyleSetup (supprimé avec le
        /// passage à APS2).</summary>
        private static CharacterPartType GetCharacterPartType(string name) {
            switch (name) {
                case "HAIR": return CharacterPartType.HAIR;
                case "EYEBROW": return CharacterPartType.EYEBROW;
                case "PANT": return CharacterPartType.PANT;
                case "SHIRT": return CharacterPartType.SHIRT;
                case "SHOES": return CharacterPartType.SHOES;
            }

            throw new Exception($"[CharacterCreationManager] Unknown part '{name}' !");
        }

        public void SetColor(Color color) {
            // APS2 : seuls cheveux (+ barbe, teintée pareil) sont recolorables — les vêtements ont
            // des matériaux fixes. La palette ne pilote donc que la couleur de cheveux ; le reste
            // sera couvert par le rework complet du launcher (V2).
            if (this.characterPartSelected == CharacterPartType.HAIR || this.characterPartSelected == CharacterPartType.EYEBROW) {
                this.characterAppearance.SetBodyColor(AdvancedPeopleSystem.BodyColorPart.Hair, color);
            }
        }

        /// <summary>Mappe les boutons historiques du menu vers les éléments APS2 (EYEBROW → barbe,
        /// seul équivalent pilosité faciale du pack).</summary>
        private static AdvancedPeopleSystem.CharacterElementType ToElementType(CharacterPartType partType) {
            switch (partType) {
                case CharacterPartType.HAIR: return AdvancedPeopleSystem.CharacterElementType.Hair;
                case CharacterPartType.EYEBROW: return AdvancedPeopleSystem.CharacterElementType.Beard;
                case CharacterPartType.SHIRT: return AdvancedPeopleSystem.CharacterElementType.Shirt;
                case CharacterPartType.PANT: return AdvancedPeopleSystem.CharacterElementType.Pants;
                case CharacterPartType.SHOES: return AdvancedPeopleSystem.CharacterElementType.Shoes;
                default: return AdvancedPeopleSystem.CharacterElementType.Hair;
            }
        }

        public void SetCurrentCharacterPart(CharacterPartType partType) {
            this.characterPartSelected = partType;

            this.characterPartButtons.ForEach(x => x.SetActive(x.PartType == partType));
        }

        public void SetGender(Gender gender) {
            this.characterAppearance.SetGender(gender);

            this.genderSelectors.ForEach(x => x.SetActive(x.Gender == gender));
        }

        public void SkinSliderChanged(Single value) {
            this.characterAppearance.SetSkinColorPercent(value);
        }
    }
    
    public enum RotatingState {
        NONE,
        LEFT,
        RIGHT
    }
}