using System;
using System.Collections.Generic;
using Sim.Utils;
using UnityEngine;
using Random = UnityEngine.Random;

public class CharacterStyleSetup : MonoBehaviour {
    [Header("Clothes Settings")]
    [SerializeField]
    private List<GameObject> hairs;

    [SerializeField]
    private List<GameObject> eyebrows;

    [SerializeField]
    private List<GameObject> shirtsMale;

    [SerializeField]
    private List<GameObject> shirtsFemale;

    [SerializeField]
    private List<GameObject> pantsMale;

    [SerializeField]
    private List<GameObject> pantsFemale;

    [SerializeField]
    private List<GameObject> bodyPartsMale;

    [SerializeField]
    private List<GameObject> bodyPartsFemale;

    [SerializeField]
    private List<GameObject> shoes;

    [Header("Skin Settings")]
    [SerializeField]
    private Color skinColorLimitMin;

    [SerializeField]
    private Color skinColorLimitMax;

    [SerializeField]
    private List<Renderer> skinMeshRenderers;

    private int currentHairIdx;
    private int currentEyebrowIdx;
    private int currentShirtIdx;
    private int currentPantIdx;
    private int currentShoesIdx;

    private Color currentHairColor = Color.white;
    private Color currentEyebrowColor = Color.white;
    private Color currentShirtColor = Color.white;
    private Color currentPantColor = Color.white;
    private Color currentShoesColor = Color.white;

    private float skinColorPercent; // 0-1

    private Gender gender = Gender.MALE;

    private void Start() {
        SelectPart(CharacterPartType.HAIR, 0);
        SelectPart(CharacterPartType.EYEBROW, 0);
        SelectPart(CharacterPartType.SHIRT, 0);
        SelectPart(CharacterPartType.PANT, 0);
        SelectPart(CharacterPartType.SHOES, 0);

        SetSkinColor(0f);
    }

    public Gender Gender => gender;

    public float SkinColorPercent => skinColorPercent;

    public void SetGender(Gender genderType) {
        this.gender = genderType;

        this.bodyPartsMale.ForEach(x => x.SetActive(this.gender == Gender.MALE));
        this.bodyPartsFemale.ForEach(x => x.SetActive(this.gender == Gender.FEMALE));

        SelectPart(CharacterPartType.SHIRT, this.currentShirtIdx);
        SelectPart(CharacterPartType.PANT, this.currentPantIdx);
    }

    public void SetSkinColor(float percent) {
        this.skinColorPercent = percent;

        Color skinColorSubstract = this.skinColorLimitMax - this.skinColorLimitMin;

        this.skinMeshRenderers.ForEach(x => {
            Material newMaterial = x.material;
            newMaterial.color = new Color {
                r = (this.skinColorLimitMin.r + (skinColorSubstract.r * percent)),
                g = (this.skinColorLimitMin.g + (skinColorSubstract.g * percent)),
                b = (this.skinColorLimitMin.b + (skinColorSubstract.b * percent))
            };
            x.material = newMaterial;
        });
    }

    public void Randomize() {
        this.currentHairColor = Random.ColorHSV();
        this.currentEyebrowColor = this.currentHairColor;
        this.currentShirtColor = Random.ColorHSV();
        this.currentPantColor = Random.ColorHSV();
        this.currentShoesColor = Random.ColorHSV();

        SelectPart(CharacterPartType.HAIR, Random.Range(0, this.hairs.Count));
        SelectPart(CharacterPartType.EYEBROW, Random.Range(0, this.eyebrows.Count));
        SelectPart(CharacterPartType.SHIRT, Random.Range(0, this.gender == Gender.MALE ? this.shirtsMale.Count : this.shirtsFemale.Count));
        SelectPart(CharacterPartType.PANT, Random.Range(0, this.gender == Gender.MALE ? this.pantsMale.Count : this.pantsFemale.Count));
        SelectPart(CharacterPartType.SHOES, Random.Range(0, this.shoes.Count));

        SetGender(Random.Range(0f, 1f) >= .5f ? Gender.MALE : Gender.FEMALE);

        SetSkinColor(Random.Range(0f, 1f));
    }

    public Style GetStyle() {
        return new Style {
            hair = new CharacterPartStyle {
                color = CommonUtils.ColorToArray(this.currentHairColor),
                idx = this.currentHairIdx
            },
            eyebrow = new CharacterPartStyle {
                color = CommonUtils.ColorToArray(this.currentEyebrowColor),
                idx = this.currentEyebrowIdx
            },
            shirt = new CharacterPartStyle {
                color = CommonUtils.ColorToArray(this.currentShirtColor),
                idx = this.currentShirtIdx
            },
            pant = new CharacterPartStyle {
                color = CommonUtils.ColorToArray(this.currentPantColor),
                idx = this.currentPantIdx
            },
            shoes = new CharacterPartStyle {
                color = CommonUtils.ColorToArray(this.currentShoesColor),
                idx = this.currentShoesIdx
            },
            skinColorPercent = this.skinColorPercent
        };
    }

    public void ApplyColor(CharacterPartType partType, Color color) {
        List<GameObject> choices = GetPartList(partType);

        int partIdx = this.GetCurrentPartIdx(partType);

        if (partIdx >= 0 && partIdx < choices.Count) {
            Renderer currentPart = choices[partIdx].GetComponent<Renderer>();
            Material newMaterial = currentPart.material;
            newMaterial.color = color;
            currentPart.material = newMaterial;
        }

        switch (partType) {
            case CharacterPartType.HAIR:
                this.currentHairColor = color;
                break;

            case CharacterPartType.EYEBROW:
                this.currentEyebrowColor = color;
                break;

            case CharacterPartType.PANT:
                this.currentPantColor = color;
                break;

            case CharacterPartType.SHIRT:
                this.currentShirtColor = color;
                break;

            case CharacterPartType.SHOES:
                this.currentShoesColor = color;
                break;
        }
    }

    public void SelectPart(CharacterPartType partType, int idx) {
        List<GameObject> choices = GetPartList(partType);

        if (partType == CharacterPartType.EYEBROW && (idx == -1 || idx == this.eyebrows.Count)) {
            idx = -1;
        } else if (idx < 0) {
            idx = choices.Count - 1;
        } else if (idx >= choices.Count) {
            idx = 0;
        }

        for (int i = 0; i < choices.Count; i++) {
            choices[i].SetActive(i == idx);
        }

        if (partType == CharacterPartType.SHIRT) {
            if (this.gender == Gender.MALE) {
                this.shirtsFemale.ForEach(x => x.SetActive(false));
            } else {
                this.shirtsMale.ForEach(x => x.SetActive(false));
            }
        } else if (partType == CharacterPartType.PANT) {
            if (this.gender == Gender.MALE) {
                this.pantsFemale.ForEach(x => x.SetActive(false));
            } else {
                this.pantsMale.ForEach(x => x.SetActive(false));
            }
        }

        switch (partType) {
            case CharacterPartType.HAIR:
                this.currentHairIdx = idx;
                this.ApplyColor(partType, this.currentHairColor);
                break;

            case CharacterPartType.EYEBROW:
                this.currentEyebrowIdx = idx;
                if (idx >= 0) this.ApplyColor(partType, this.currentEyebrowColor);
                break;

            case CharacterPartType.PANT:
                this.currentPantIdx = idx;
                this.ApplyColor(partType, this.currentPantColor);
                break;

            case CharacterPartType.SHIRT:
                this.currentShirtIdx = idx;
                this.ApplyColor(partType, this.currentShirtColor);
                break;

            case CharacterPartType.SHOES:
                this.currentShoesIdx = idx;
                this.ApplyColor(partType, this.currentShoesColor);
                break;
        }
    }

    public int GetCurrentPartIdx(CharacterPartType partType) {
        switch (partType) {
            case CharacterPartType.HAIR:
                return this.currentHairIdx;

            case CharacterPartType.EYEBROW:
                return this.currentEyebrowIdx;

            case CharacterPartType.PANT:
                return this.currentPantIdx;

            case CharacterPartType.SHIRT:
                return this.currentShirtIdx;

            case CharacterPartType.SHOES:
                return this.currentShoesIdx;
        }

        throw new Exception("[CharacterStyleSetup] Unknown part !");
    }
    
    public Color GetCurrentPartColor(CharacterPartType partType) {
        switch (partType) {
            case CharacterPartType.HAIR:
                return this.currentHairColor;

            case CharacterPartType.EYEBROW:
                return this.currentEyebrowColor;

            case CharacterPartType.PANT:
                return this.currentPantColor;

            case CharacterPartType.SHIRT:
                return this.currentShirtColor;

            case CharacterPartType.SHOES:
                return this.currentShoesColor;
        }

        throw new Exception("[CharacterStyleSetup] Unknown part !");
    }

    public static CharacterPartType GetCharacterPartType(string name) {
        switch (name) {
            case "HAIR":
                return CharacterPartType.HAIR;

            case "EYEBROW":
                return CharacterPartType.EYEBROW;

            case "PANT":
                return CharacterPartType.PANT;

            case "SHIRT":
                return CharacterPartType.SHIRT;

            case "SHOES":
                return CharacterPartType.SHOES;
        }

        throw new Exception("[CharacterStyleSetup] Unknown part !");
    }

    private List<GameObject> GetPartList(CharacterPartType partType) {
        switch (partType) {
            case CharacterPartType.HAIR:
                return hairs;

            case CharacterPartType.EYEBROW:
                return eyebrows;

            case CharacterPartType.PANT:
                return this.gender == Gender.MALE ? pantsMale : pantsFemale;

            case CharacterPartType.SHIRT:
                return this.gender == Gender.MALE ? shirtsMale : shirtsFemale;

            case CharacterPartType.SHOES:
                return shoes;
        }

        throw new Exception("[CharacterStyleSetup] Unknown part !");
    }
}