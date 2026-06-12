using System;
using Mirror;
using Sim.Entities;
using Sim.Enums;
using Sim.Missions;
using Sim.Scriptables;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim {
    /// <summary>
    /// "Voir identité" social card. AUTHORED in HUD Manager.prefab (pattern of
    /// BuyConfirmUI / AcquaintanceRequestUI): the panel hosts this component,
    /// registers Instance + the close listener in Awake, then hides itself. Shows
    /// name, mood icon, current job ("voie de vie") and meeting date. Opened from
    /// the radial menu (live player) or the Contacts app (by id).
    /// </summary>
    public class IdentityCardUI : MonoBehaviour {
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text jobText;
        [SerializeField] private TMP_Text metText;
        [SerializeField] private Image moodImage;
        [SerializeField] private Button closeButton;

        [Header("Remove contact")]
        [SerializeField] private Button removeButton;        // "Retirer des contacts" (contacts only)
        [SerializeField] private GameObject confirmPanel;    // confirmation sub-panel (hidden by default)
        [SerializeField] private Button confirmRemoveButton;
        [SerializeField] private Button cancelRemoveButton;

        public static IdentityCardUI Instance;

        private string _currentCharacterId;

        private void Awake() {
            if (Instance != null && Instance != this) { Destroy(this.gameObject); return; }
            Instance = this;
            if (closeButton != null) closeButton.onClick.AddListener(Hide);
            if (removeButton != null) removeButton.onClick.AddListener(ShowRemoveConfirm);
            if (cancelRemoveButton != null) cancelRemoveButton.onClick.AddListener(HideRemoveConfirm);
            if (confirmRemoveButton != null) confirmRemoveButton.onClick.AddListener(ConfirmRemove);
            this.gameObject.SetActive(false);
        }

        private void OnDestroy() {
            if (Instance == this) Instance = null;
        }

        /// <summary>From the radial menu — live data of an online player.</summary>
        public void ShowFor(PlayerController player) {
            if (player?.CharacterData == null) return;
            CharacterData cd = player.CharacterData;

            _currentCharacterId = cd.Id;
            string metAt = ClientRelationshipManager.Instance.TryGet(cd.Id, out RelationshipEntry e) ? e.MetAt : null;
            Populate(cd.Identity.FullName, cd.Mood, cd.CurrentProfessionId, metAt, hasMood: true);
        }

        /// <summary>From the Contacts app — store data (target may be offline → no mood).</summary>
        public void ShowFor(string characterId) {
            if (!ClientRelationshipManager.Instance.TryGet(characterId, out RelationshipEntry e)) return;
            _currentCharacterId = characterId;
            Populate(e.FullName, default, e.JobProfessionId, e.MetAt, hasMood: false);
        }

        public void Hide() {
            this.gameObject.SetActive(false);
        }

        private void Populate(string fullName, MoodEnum mood, string professionId, string metAtIso, bool hasMood) {
            if (nameText != null) nameText.text = string.IsNullOrEmpty(fullName) ? "Broz inconnu" : fullName;
            if (jobText != null) {
                var prof = string.IsNullOrEmpty(professionId) ? null : Sim.Professions.ProfessionDatabase.ById(professionId);
                jobText.text = $"Voie de vie : {(prof != null ? prof.displayName : "Sans emploi")}";
            }
            if (metText != null) metText.text = $"Rencontré le : {FormatDate(metAtIso)}";

            if (moodImage != null) {
                if (hasMood) {
                    MoodConfig cfg = DatabaseManager.GetMoodConfigByEnum(mood);
                    moodImage.sprite = cfg != null ? cfg.Sprite : null;
                    moodImage.enabled = moodImage.sprite != null;
                } else {
                    moodImage.enabled = false;
                }
            }

            // "Retirer des contacts" only makes sense for an actual contact.
            bool isContact = ClientRelationshipManager.Instance.GetState(_currentCharacterId) == RelationshipState.Contact;
            if (removeButton != null) removeButton.gameObject.SetActive(isContact);
            HideRemoveConfirm();

            this.gameObject.SetActive(true);
        }

        private void ShowRemoveConfirm() {
            if (confirmPanel != null) confirmPanel.SetActive(true);
        }

        private void HideRemoveConfirm() {
            if (confirmPanel != null) confirmPanel.SetActive(false);
        }

        private void ConfirmRemove() {
            if (!string.IsNullOrEmpty(_currentCharacterId) && NetworkClient.active) {
                NetworkClient.Send(new C2S_RemoveContact { characterId = _currentCharacterId });
            }
            HideRemoveConfirm();
            Hide();
        }

        private static string FormatDate(string iso) {
            if (string.IsNullOrEmpty(iso)) return "—";
            return DateTime.TryParse(iso, out DateTime d) ? d.ToLocalTime().ToString("dd/MM/yyyy") : iso;
        }
    }
}
