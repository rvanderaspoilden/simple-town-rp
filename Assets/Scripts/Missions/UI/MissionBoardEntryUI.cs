using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.Missions {
    /// <summary>
    /// Une ligne du board. Préfab à composer dans l'éditeur :
    /// - Background Image
    /// - Title TMP_Text
    /// - Status TMP_Text
    /// - Owner TMP_Text
    /// - Reward TMP_Text (optionnel — affiche les récompenses de la mission)
    /// - Take Button (visible uniquement pour les missions Available)
    /// </summary>
    public class MissionBoardEntryUI : MonoBehaviour {
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text ownerText;
        [SerializeField] private TMP_Text rewardText;
        [SerializeField] private Button takeButton;
        [SerializeField] private Image statusBadge;

        [Header("Couleurs (Available / Active / Locked)")]
        [SerializeField] private Color availableColor = new Color(0.4f, 0.8f, 0.4f);
        [SerializeField] private Color activeColor = new Color(0.4f, 0.6f, 0.9f);
        [Tooltip("Teinte du badge quand la mission est verrouillée par un nœud de constellation non débloqué.")]
        [SerializeField] private Color lockedColor = new Color(0.6f, 0.6f, 0.6f);

        [Tooltip("Séparateur entre les récompenses (ex. ' · ' ou ' + ').")]
        [SerializeField] private string rewardSeparator = " · ";

        private string _instanceId;
        private Action<string> _onTake;

        public void Bind(MissionBoardEntry entry, Action<string> onTake) {
            _instanceId = entry.instanceId;
            _onTake = onTake;

            var def = MissionDatabase.GetById(entry.missionId);
            titleText.text = def != null ? def.DisplayNameKey : entry.missionId;

            bool available = entry.Status == MissionStatus.Available;
            // Node gate (visual mirror of MissionServerManager.TakeFromBoard) : a mission
            // can require a constellation node to be unlocked. Resolved against the local
            // player's provider — the server still re-validates on Take.
            bool nodeUnlocked = IsRequiredNodeUnlocked(def);
            string requirement = available && !nodeUnlocked ? RequirementLabel(def) : null;

            if (available)
                statusText.text = nodeUnlocked ? "Disponible" : $"🔒 Requiert : {requirement}";
            else
                statusText.text = $"En cours · étape {entry.currentStepIndex + 1}";

            ownerText.text = available ? string.Empty : (string.IsNullOrEmpty(entry.ownerName) ? "—" : entry.ownerName);

            if (statusBadge != null)
                statusBadge.color = !available ? activeColor : (nodeUnlocked ? availableColor : lockedColor);

            if (rewardText != null) rewardText.text = BuildRewardSummary(def);

            bool hasActiveJob = MissionClientManager.Instance.States.Count > 0;
            bool showTake = available && !hasActiveJob;
            takeButton.gameObject.SetActive(showTake);
            // Greyed (non-interactable) when the required node isn't unlocked yet.
            takeButton.interactable = showTake && nodeUnlocked;
            takeButton.onClick.RemoveAllListeners();
            if (showTake && nodeUnlocked) takeButton.onClick.AddListener(OnTakeClicked);
        }

        private static bool IsRequiredNodeUnlocked(MissionDefinition def) {
            var node = def != null ? def.RequiredNode : null;
            if (node == null) return true; // no gate
            var lp = Mirror.NetworkClient.localPlayer;
            var pc = lp != null ? lp.GetComponent<Sim.Player.PlayerConstellation>() : null;
            var provider = pc != null ? pc.Provider : null;
            // Fail-open if the provider isn't ready: the server still enforces the gate
            // on Take, so we don't want to grey out everything during a brief unhydrated
            // window.
            return provider == null || provider.State.IsUnlocked(node);
        }

        private static string RequirementLabel(MissionDefinition def) {
            var node = def != null ? def.RequiredNode : null;
            if (node == null) return string.Empty;
            return string.IsNullOrEmpty(node.displayName) ? node.id : node.displayName;
        }

        private string BuildRewardSummary(MissionDefinition def) {
            if (def == null) return string.Empty;

            var sb = new StringBuilder();
            // Source courante : RewardEntries (kind partagé + montant authoré).
            var entries = def.RewardEntries;
            if (entries != null && entries.Count > 0) {
                for (int i = 0; i < entries.Count; i++) {
                    var e = entries[i];
                    if (e == null || e.kind == null) continue;
                    var s = e.kind.GetDisplayString(e.amount);
                    if (string.IsNullOrEmpty(s)) continue;
                    if (sb.Length > 0) sb.Append(rewardSeparator);
                    sb.Append(s);
                }
                if (sb.Length > 0) return sb.ToString();
            }

            // Fallback legacy (anciennes missions non migrées vers RewardEntries).
            if (def.Rewards != null) {
                for (int i = 0; i < def.Rewards.Count; i++) {
                    var r = def.Rewards[i];
                    if (r == null) continue;
                    var s = r.GetDisplayString();
                    if (string.IsNullOrEmpty(s)) continue;
                    if (sb.Length > 0) sb.Append(rewardSeparator);
                    sb.Append(s);
                }
            }
            return sb.ToString();
        }

        private void OnTakeClicked() {
            _onTake?.Invoke(_instanceId);
        }
    }
}
