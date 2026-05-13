using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sim.Jobs {
    /// <summary>
    /// Une ligne du board. Préfab à composer dans l'éditeur :
    /// - Background Image
    /// - Title TMP_Text
    /// - Status TMP_Text
    /// - Owner TMP_Text
    /// - Take Button (visible uniquement pour les missions Available)
    /// </summary>
    public class JobBoardEntryUI : MonoBehaviour {
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text ownerText;
        [SerializeField] private Button takeButton;
        [SerializeField] private Image statusBadge;

        [Header("Couleurs (Available / Active)")]
        [SerializeField] private Color availableColor = new Color(0.4f, 0.8f, 0.4f);
        [SerializeField] private Color activeColor = new Color(0.4f, 0.6f, 0.9f);

        private string _instanceId;
        private Action<string> _onTake;

        public void Bind(JobBoardEntry entry, Action<string> onTake) {
            _instanceId = entry.instanceId;
            _onTake = onTake;

            var def = JobDatabase.GetById(entry.jobId);
            titleText.text = def != null ? def.DisplayNameKey : entry.jobId;

            bool available = entry.Status == JobStatus.Available;
            statusText.text = available ? "Disponible" : $"En cours · étape {entry.currentStepIndex + 1}";
            ownerText.text = available ? string.Empty : (string.IsNullOrEmpty(entry.ownerName) ? "—" : entry.ownerName);

            if (statusBadge != null) statusBadge.color = available ? availableColor : activeColor;

            takeButton.gameObject.SetActive(available);
            takeButton.onClick.RemoveAllListeners();
            if (available) takeButton.onClick.AddListener(OnTakeClicked);
        }

        private void OnTakeClicked() {
            _onTake?.Invoke(_instanceId);
        }
    }
}
