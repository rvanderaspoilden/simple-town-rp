using System.Collections;
using Newtonsoft.Json;
using Sim.Entities;
using Sim.Entities.Persistence;
using Sim.Missions;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

namespace Sim {
    public class HomePanel : MonoBehaviour {
        [Header("Player card")]
        [SerializeField]
        private TextMeshProUGUI greetingText;

        [SerializeField]
        private TextMeshProUGUI levelText;

        [SerializeField]
        private TextMeshProUGUI moneyText;

        [Header("Version")]
        [SerializeField]
        private TextMeshProUGUI versionText;

        [Header("Style preview")]
        [SerializeField]
        private CharacterAppearance characterAppearance;

        private Coroutine wornClothingFetch;

        private void Awake() {
            if (versionText != null) {
                versionText.text = $"DÉMO (version {Application.version})\nWORK IN PROGRESS";
            }
        }

        public void Bind(CharacterData character) {
            if (character == null) return;

            if (greetingText != null) {
                string firstname = character.Identity.Firstname;
                greetingText.text = string.IsNullOrEmpty(firstname)
                    ? "Salut, Citoyen !"
                    : $"Salut, {firstname} !";
            }

            if (levelText != null) {
                var prof = character.CurrentProfession;
                levelText.text = prof != null ? prof.displayName : "Sans emploi";
            }

            if (moneyText != null) {
                moneyText.text = character.Money.ToString("N0");
            }

            if (characterAppearance != null) {
                // Les vêtements portés vivent en ITEMS (place wearables) — le style ne porte
                // que le corps après migration. On applique le style puis on fetch la place
                // pour habiller le preview ; un perso jamais migré a encore ses vêtements
                // dans le style (ApplyStyle suffit, la place n'existe pas).
                characterAppearance.ClearWornClothing();
                characterAppearance.ApplyStyle(character.Style);
                if (wornClothingFetch != null) StopCoroutine(wornClothingFetch);
                wornClothingFetch = StartCoroutine(FetchWornClothingCoroutine(character.Id));
            }
        }

        private IEnumerator FetchWornClothingCoroutine(string characterId) {
            if (ApiManager.Instance == null || string.IsNullOrEmpty(characterId)) yield break;

            PlaceJson place = null;
            yield return ApiManager.Instance.LookupPlaceByKeyCoroutine($"wearables:{characterId}", p => place = p);
            if (place == null || string.IsNullOrEmpty(place.Id)) yield break;

            UnityWebRequest req = ApiManager.Instance.GetPlaceStateRequest(place.Id);
            yield return req.SendWebRequest();
            if (req.responseCode != 200) yield break;

            PlaceStateJson state = null;
            try { state = JsonConvert.DeserializeObject<PlaceStateJson>(req.downloadHandler.text); }
            catch { yield break; }

            var ids = new string[ClothingConfig.WearSlotCount];
            if (state?.items != null) {
                foreach (var it in state.items) {
                    int slot = ReadSlotIndex(it.stateData);
                    if (slot >= 0 && slot < ids.Length) ids[slot] = it.configId.ToString();
                }
            }

            if (characterAppearance != null) {
                characterAppearance.SetWornClothing(string.Join(",", ids));
            }
        }

        private static int ReadSlotIndex(System.Collections.Generic.Dictionary<string, object> stateData) {
            if (stateData == null || !stateData.TryGetValue("slotIndex", out var v) || v == null) return 0;
            if (v is long l) return (int)l;
            if (v is int i) return i;
            if (v is double d) return (int)d;
            int parsed;
            return int.TryParse(v.ToString(), out parsed) ? parsed : 0;
        }

        public void Show() {
            this.gameObject.SetActive(true);
        }

        public void Hide() {
            this.gameObject.SetActive(false);
        }
    }
}
